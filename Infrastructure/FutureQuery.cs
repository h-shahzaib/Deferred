using FastMember;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Deferred.Infrastructure
{
    public class FutureQuery<T> : IEnumerable<T>
    {
        public bool HasValue { get; private set; }
        public List<T> Items { get; private set; }

        public void SetResults(List<T> entities)
        {
            Items = entities;
            HasValue = true;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public IEnumerator<T> GetEnumerator()
        {
            if (!HasValue)
                throw new InvalidOperationException($"Values have not been fetched yet.");

            foreach (T item in Items)
                yield return item;
        }
    }
}
