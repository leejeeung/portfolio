
using System;
using System.Collections.Generic;

namespace jjevol
{
    public abstract class KeyedTable<TKey, TData> : TableBase
        where TKey : notnull
        where TData : TableData
    {
        protected KeyedTable(string tableName) : base(tableName)
        {
        }

        public override Type RowType => typeof(TData);

        protected readonly Dictionary<TKey, TData> _dict = new();

        protected abstract TKey GetKey(TData data);
        protected abstract TData ParseJson(TSVRow json);

        protected override void InsertData(List<TSVRow> datas)
        {
            _dict.Clear();
            if (datas == null) return;

            foreach (var json in datas)
            {
                var data = ParseJson(json);
                var key = GetKey(data);

                _InsertData(key, data);
            }
        }

        protected virtual void _InsertData(TKey key, TData data)
        {
            _dict[key] = data;
        }

        protected override void _DataClear()
        {
            _dict.Clear();
        }

        public TData Get(TKey key)
        {
            _dict.TryGetValue(key, out var value);
            return value;
        }
    }
}