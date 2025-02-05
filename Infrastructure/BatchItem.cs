using System.Data.Common;

namespace Deferred.Infrastructure
{
    public class BatchItem
    {
        public object Future { get; set; }
        public DbCommand Command { get; set; }
    }
}
