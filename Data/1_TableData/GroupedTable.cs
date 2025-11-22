// RHGroupedTable: 그룹형 테이블 (Key로 묶인 List)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace jjevol
{
    public abstract class GroupedTable<TKey, TData> : TableBase
        where TData : TableData
        where TKey : notnull
    {
        public override Type RowType => typeof(TData);

        protected readonly Dictionary<TKey, List<TData>> _dict = new();

        protected GroupedTable(string tableName) : base(tableName)
        {
        }

        protected abstract TData ParseJson(TSVRow json);
        protected abstract TKey GetKey(TData data);

        protected override void InsertData(List<TSVRow> datas)
        {
            _dict.Clear();
            if (datas == null) return;

            foreach (var json in datas)
            {
                var data = ParseJson(json);
                var key = GetKey(data);

                if (!_dict.ContainsKey(key))
                    _dict[key] = new List<TData>();

                _dict[key].Add(data);
            }
        }

        public IReadOnlyDictionary<TKey, List<TData>> Dict => _dict;

        public List<TData> GetGroup(TKey key)
        {
            return _dict.TryGetValue(key, out var list) ? list : null;
        }

        protected override void _DataClear()
        {
            _dict.Clear();
        }
    }
}