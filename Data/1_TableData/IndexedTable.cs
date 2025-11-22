using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace jjevol
{
    public abstract class IndexedTable<TData> : TableBase where TData : TableData
    {
        public override Type RowType => typeof(TData);
        protected Dictionary<int, TData> _dict { get; } = new();

        protected IndexedTable(string tableName) : base(tableName)
        {
        }

        protected abstract TData ParseJson(TSVRow data);

        protected override void InsertData(List<TSVRow> datas)
        {
            _dict.Clear();

            if (datas == null) return;

            foreach (var json in datas)
            {
                var data = ParseJson(json);

                if (_dict.ContainsKey(data.id))
                    _dict[data.id] = data; // 덮어쓰기
                else
                    _dict.Add(data.id, data);
            }
        }

        protected override void _DataClear()
        {
            _dict.Clear();
        }

        public IReadOnlyDictionary<int, TData> Dict => _dict;

        public IReadOnlyList<TData> Datas => new List<TData>(_dict.Values);

        public TData Get(int id)
        {
            if (_dict.ContainsKey(id)) return _dict[id];

            return null;
        }
    }
}