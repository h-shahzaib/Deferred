using FastMember;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Deferred.Infrastructure
{
    public class DefferedContext
    {
        private readonly string m_ConnectionString;
        private readonly List<BatchItem> m_BatchItems;

        public DefferedContext(string connection_string)
        {
            m_ConnectionString = connection_string;
            m_BatchItems = new List<BatchItem>();
        }

        public async Task ExecuteAsync()
        {
            using (var cnn = new SqlConnection(m_ConnectionString))
            {
                DbCommand merged_command = cnn.CreateCommand();
                foreach (DbCommand command in m_BatchItems.Select(i => i.Command))
                    merged_command.CommandText += GetParameterizedQuery(command) + ";\n\n";
                merged_command.CommandText = merged_command.CommandText.RemoveLastChar(2);

                cnn.Open();
                var reader = await merged_command.ExecuteReaderAsync();
                int result_index = 0;

                do
                {
                    var future_query = m_BatchItems[result_index].Future;
                    var future_type = future_query.GetType();
                    var generic_type = future_type.GetGenericArguments()[0];
                    var method = typeof(DefferedContext).GetMethod("ReaderToList", BindingFlags.Static | BindingFlags.Public);
                    var generic_method = method.MakeGenericMethod(generic_type);
                    var list = generic_method.Invoke(null, new object[] { reader });
                    if (future_type.Name.StartsWith("FutureQuery"))
                        future_type.GetMethod("SetResults").Invoke(future_query, new object[] { list });
                    else if (future_type.Name.StartsWith("FutureValueQuery"))
                    {
                        var list_type = list.GetType();
                        var firstOrDefault_method = typeof(Enumerable).GetMethods()
                            .FirstOrDefault(m => m.Name == "FirstOrDefault" && m.GetParameters().Length == 1)
                            .MakeGenericMethod(list_type.GetGenericArguments()[0]);
                        var first_value = firstOrDefault_method.Invoke(null, new object[] { list });
                        future_type.GetMethod("SetValue").Invoke(future_query, new object[] { first_value });
                    }

                    result_index++;
                }
                while (reader.NextResult());

                reader.Close();
                cnn.Close();
                m_BatchItems.Clear();
            }
        }

        private string GetParameterizedQuery(DbCommand command)
        {
            string query_string = command.CommandText;
            foreach (SqlParameter parameter in command.Parameters)
                if (parameter.SqlDbType == SqlDbType.Bit || parameter.SqlDbType == SqlDbType.Text)
                    query_string = query_string.Replace(parameter.ParameterName, $"'{parameter.Value}'");
                else if (parameter.SqlDbType == SqlDbType.Int || parameter.SqlDbType == SqlDbType.BigInt || parameter.SqlDbType == SqlDbType.Float)
                    query_string = query_string.Replace(parameter.ParameterName, parameter.Value.ToString());
                else throw new Exception($"SqlDbType: '{parameter.SqlDbType}' having Value: '{parameter.Value}' is not yet supported.");
            return query_string;
        }

        /* --------------------------------------------------------------------------------------------------------------------------------- */

        public DefferedQuery<T> InFuture<T>(IQueryable<T> queryable)
        {
            var future = new DefferedQuery<T>();
            var batch = new BatchItem();
            batch.Future = future;
            batch.Command = queryable.CreateDbCommand();
            m_BatchItems.Add(batch);
            return future;
        }

        /* --------------------------------------------------------------------------------------------------------------------------------- */

        public DefferedValueQuery<int> DeferredSum<T>(IQueryable<T> queryable, Expression<Func<T, int>> selector) => DeferredForValues<int>(queryable, $"CAST(SUM({selector.GetPropertyInfo().Name}) AS int)");
        public DefferedValueQuery<int?> DeferredSum<T>(IQueryable<T> queryable, Expression<Func<T, int?>> selector) => DeferredForValues<int?>(queryable, $"CAST(SUM({selector.GetPropertyInfo().Name}) AS int)");
        public DefferedValueQuery<long> DeferredSum<T>(IQueryable<T> queryable, Expression<Func<T, long>> selector) => DeferredForValues<long>(queryable, $"CAST(SUM({selector.GetPropertyInfo().Name}) AS bigint)");
        public DefferedValueQuery<long?> DeferredSum<T>(IQueryable<T> queryable, Expression<Func<T, long?>> selector) => DeferredForValues<long?>(queryable, $"CAST(SUM({selector.GetPropertyInfo().Name}) AS bigint)");
        public DefferedValueQuery<int> DeferredCount(IQueryable queryable) => DeferredForValues<int>(queryable, "COUNT(*)");
        public DefferedValueQuery<long> DeferredLongCount(IQueryable queryable) => DeferredForValues<long>(queryable, "CAST(COUNT(*) AS bigint)");

        private DefferedValueQuery<T> DeferredForValues<T>(IQueryable queryable, string Function)
        {
            var command = queryable.CreateDbCommand();
            var regex = new Regex("SELECT.+\nFROM");
            command.CommandText = regex.Replace(command.CommandText, $"SELECT {Function}\nFROM");
            return AddToBatch<T>(command);
        }

        private DefferedValueQuery<T> AddToBatch<T>(DbCommand command)
        {
            var future = new DefferedValueQuery<T>();
            var batch = new BatchItem();
            batch.Future = future;
            batch.Command = command;
            m_BatchItems.Add(batch);
            return future;
        }

        /* --------------------------------------------------------------------------------------------------------------------------------- */

        public DefferedValueQuery<T> DeferredFirstOrDefault<T>(IQueryable<T> queryable) => AddToBatch(queryable.Take(1));
        public DefferedValueQuery<T> DeferredSkip<T>(IQueryable<T> queryable, int count) => AddToBatch(queryable.Skip(count));
        public DefferedValueQuery<T> DeferredTake<T>(IQueryable<T> queryable, int count) => AddToBatch(queryable.Take(count));

        private DefferedValueQuery<T> AddToBatch<T>(IQueryable<T> queryable)
        {
            var future = new DefferedValueQuery<T>();
            var batch = new BatchItem();
            batch.Future = future;
            batch.Command = queryable.CreateDbCommand();
            m_BatchItems.Add(batch);
            return future;
        }

        /* --------------------------------------------------------------------------------------------------------------------------------- */

        public static List<T> ReaderToList<T>(IDataReader reader)
        {
            var list = new List<T>();

            var t_type = typeof(T);
            if (t_type.IsAnonymousType())
            {
                while (reader.Read())
                {
                    var parameters = new List<object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.GetValue(i);
                        if (value.GetType() != typeof(DBNull))
                            parameters.Add(value);
                        else parameters.Add(null);
                    }

                    var constructor = t_type.GetConstructors()[0];
                    var anonymous_object = constructor.Invoke(parameters.ToArray());
                    list.Add((T)anonymous_object);
                }
            }
            else if (t_type.IsClass)
            {
                var accessor = TypeAccessor.Create(typeof(T));
                while (reader.Read())
                {
                    var obj = accessor.CreateNew();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.GetValue(i);
                        if (value.GetType() != typeof(DBNull))
                            accessor[obj, columnName] = value;
                        else accessor[obj, columnName] = null;
                    }
                    list.Add((T)obj);
                }
            }
            else if (t_type.IsValueType && reader.FieldCount == 1)
            {
                while (reader.Read())
                {
                    var value = reader.GetValue(0);
                    if (value.GetType() != typeof(DBNull))
                        list.Add((T)value);
                    else list.Add(default);
                }
            }
            else throw new Exception($"Type: '{t_type.Name}' is not yet supported.");

            return list;
        }
    }
}
