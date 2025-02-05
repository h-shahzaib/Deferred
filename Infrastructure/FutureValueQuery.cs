using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Deferred.Infrastructure
{
    public class FutureValueQuery<T>
    {
        public bool HasValue { get; set; }
        public T Value
        {
            get
            {
                if (!HasValue)
                    throw new InvalidOperationException($"Value have not been fetched yet.");
                return value;
            }
            set => this.value = value;
        }

        public void SetValue(T value)
        {
            Value = value;
            HasValue = true;
        }

        private T value;
    }
}
