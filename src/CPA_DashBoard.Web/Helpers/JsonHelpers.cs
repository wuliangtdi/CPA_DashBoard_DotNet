using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CPA_DashBoard.Web.Helpers;

/// <summary>
/// 提供和 JsonNode 相关的通用辅助方法。
/// </summary>
public static class JsonHelpers
{
    /// <summary>
    /// 将任意 JSON 字符串解析为 JsonObject。
    /// </summary>
    public static JsonObject ParseObject(string jsonText)
    {
        return JsonNode.Parse(string.IsNullOrWhiteSpace(jsonText) ? "{}" : jsonText)?.AsObject() ?? new JsonObject();
    }

    /// <summary>
    /// 克隆一个 JsonObject，避免引用共享导致数据串改。
    /// </summary>
    public static JsonObject CloneObject(JsonObject source)
    {
        return ParseObject(source.ToJsonString());
    }

    /// <summary>
    /// 安全读取字符串字段。
    /// </summary>
    public static string GetString(this JsonObject jsonObject, string propertyName, string defaultValue = "")
    {
        return jsonObject[propertyName]?.GetValue<string?>() ?? defaultValue;
    }

    /// <summary>
    /// 安全读取布尔字段。
    /// </summary>
    public static bool GetBoolean(this JsonObject jsonObject, string propertyName, bool defaultValue = false)
    {
        return jsonObject[propertyName]?.GetValue<bool?>() ?? defaultValue;
    }

    /// <summary>
    /// 安全读取双精度数字段。
    /// </summary>
    public static double GetDouble(this JsonObject jsonObject, string propertyName, double defaultValue = 0)
    {
        return jsonObject[propertyName]?.GetValue<double?>() ?? defaultValue;
    }

    /// <summary>
    /// 安全读取长整数字段。
    /// </summary>
    public static long GetInt64(this JsonObject jsonObject, string propertyName, long defaultValue = 0)
    {
        return jsonObject[propertyName]?.GetValue<long?>() ?? defaultValue;
    }

    /// <summary>
    /// 尝试把节点转换成对象。
    /// </summary>
    public static JsonObject? ToObject(this JsonNode? node)
    {
        return node as JsonObject;
    }

    /// <summary>
    /// 尝试把节点转换成数组。
    /// </summary>
    public static JsonArray? ToArray(this JsonNode? node)
    {
        return node as JsonArray;
    }

    /// <summary>
    /// 把任意对象序列化为 JsonNode。
    /// </summary>
    public static JsonNode? ToNode(object? value)
    {
        return value is null ? null : JsonSerializer.SerializeToNode(value);
    }

    /// <summary>
    /// 将 DateTimeOffset 转成 Unix 秒级时间戳。
    /// </summary>
    public static long ToUnixTimeSeconds(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToUnixTimeSeconds();
    }

    /// <summary>
    /// 尝试把文本转成整数；失败时返回默认值。
    /// </summary>
    public static int ToInt32(string? text, int defaultValue)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;
    }
}
