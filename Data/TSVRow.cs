using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public class TSVRow
{
    private Dictionary<string, string> mData;

    public TSVRow(Dictionary<string, string> data)
    {
        mData = data;
    }

    public static List<TSVRow> FromTsv(string tsvText)
    {
        var list = new List<TSVRow>();
        if (string.IsNullOrEmpty(tsvText)) return list;

        var lines = tsvText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return list;

        var headers = lines[0].Split('\t');

        for (int i = 1; i < lines.Length; i++)
        {
            var fields = lines[i].Split('\t');
            var row = new Dictionary<string, string>();
            for (int j = 0; j < headers.Length && j < fields.Length; j++)
            {
                row[headers[j]] = fields[j];
            }
            list.Add(new TSVRow(row));
        }

        return list;
    }

    public bool Contains(string name)
    {
        return mData != null && mData.ContainsKey(name);
    }

    public string Get(string name)
    {
        return mData != null && mData.TryGetValue(name, out var value) ? value : null;
    }

    private T GetValue<T>(string name, T defaultValue = default)
    {
        var value = Get(name);
        if (string.IsNullOrEmpty(value)) return defaultValue;

        try
        {
            if (typeof(T).IsEnum)
                return (T)Enum.Parse(typeof(T), value, true);

            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    public int GetInt(string name, int defaultValue = 0) => GetValue<int>(name, defaultValue);
    public float GetFloat(string name, float defaultValue = 0f) => GetValue<float>(name, defaultValue);
    public double GetDouble(string name, double defaultValue = 0d) => GetValue<double>(name, defaultValue);
    public bool GetBoolean(string name, bool defaultValue = false) => GetValue<bool>(name, defaultValue);
    public long GetLong(string name, long defaultValue = 0L) => GetValue<long>(name, defaultValue);
    public string GetString(string name, string defaultValue = "") => GetValue<string>(name, defaultValue);
    public decimal GetDecimal(string name, decimal defaultValue = 0L) => GetValue<decimal>(name, defaultValue);

    public List<string> GetStringList(string name, char separator = '/')
    {
        var value = Get(name);
        return string.IsNullOrEmpty(value) ? new List<string>() :
            value.Split(separator).Select(s => s.Trim()).ToList();
    }

    public List<int> GetIntList(string name, char separator = '/')
    {
        return GetStringList(name, separator)
            .Select(s => int.TryParse(s, out var v) ? v : 0).ToList();
    }

    public List<long> GetLongList(string name, char separator = '/')
    {
        return GetStringList(name, separator)
            .Select(s => long.TryParse(s, out var v) ? v : 0L).ToList();
    }

    public List<float> GetFloatList(string name, char separator = '/')
    {
        return GetStringList(name, separator)
            .Select(s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f).ToList();
    }

    public List<double> GetDoubleList(string name, char separator = '/')
    {
        return GetStringList(name, separator)
            .Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0d).ToList();
    }

    public List<bool> GetBooleanList(string name, char separator = '/')
    {
        return GetStringList(name, separator)
            .Select(s => bool.TryParse(s, out var v) && v).ToList();
    }

    public override string ToString()
    {
        return string.Join(", ", mData.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
    }
}
