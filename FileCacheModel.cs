using System;
using System.Collections.Generic;
using System.Text;

namespace CoreFileCache
{
    [Serializable]
    public class FileCacheModel
    {
        public DateTime ExpireTime { get; set; }
        public DateTime RefreshTime { get; set; }
        public DateTime? AbsoluteExpireTime { get; set; }
        public long SlidingExprireSecond { get; set; }
    }
}
