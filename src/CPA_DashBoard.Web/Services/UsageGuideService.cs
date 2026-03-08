using System.Text.Json.Nodes;

namespace CPA_DashBoard.Web.Services;

/// <summary>
/// 负责生成 API 使用说明与示例代码。
/// </summary>
public sealed class UsageGuideService
{
    /// <summary>
    /// 保存应用上下文服务实例。
    /// </summary>
    private readonly AppContextService _appContextService;

    /// <summary>
    /// 使用应用上下文服务初始化使用说明服务。
    /// </summary>
    public UsageGuideService(AppContextService appContextService)
    {
        // 这里保存应用上下文服务，供后续读取 API 地址和密钥配置。
        _appContextService = appContextService;
    }

    /// <summary>
    /// 获取前端展示所需的 API 使用说明。
    /// </summary>
    public JsonObject GetUsageGuide()
    {
        // 这里优先取第一把可用 API Key，没有时回退成占位文本。
        var apiKey = _appContextService.Settings.ApiKeys.FirstOrDefault() ?? "YOUR_API_KEY";

        // 这里拼出前端和示例代码都要用到的基础访问地址。
        var baseUrl = $"http://{_appContextService.Settings.ApiHost}:{_appContextService.Settings.ApiPort}";

        // 这里生成 curl 的非流式调用示例。
        var curlExample = $$$"""
curl {{{baseUrl}}}/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer {{{apiKey}}}" \
  -d '{{
    "model": "gemini-2.5-flash",
    "messages": [
      {{"role": "user", "content": "Hello, how are you?"}}
    ]
  }}'
""";

        // 这里生成 Python requests 的调用示例。
        var pythonRequestsExample = $$$"""
import requests

url = "{{{baseUrl}}}/v1/chat/completions"
headers = {
    "Content-Type": "application/json",
    "Authorization": "Bearer {{{apiKey}}}"
}
data = {
    "model": "gemini-2.5-flash",
    "messages": [
        {"role": "user", "content": "Hello, how are you?"}
    ]
}

response = requests.post(url, headers=headers, json=data)
print(response.json())
""";

        // 这里生成 Python OpenAI SDK 的非流式调用示例。
        var pythonOpenAiExample = $$$"""
from openai import OpenAI

client = OpenAI(
    api_key="{{{apiKey}}}",
    base_url="{{{baseUrl}}}/v1"
)

response = client.chat.completions.create(
    model="gemini-2.5-flash",
    messages=[
        {"role": "user", "content": "Hello, how are you?"}
    ]
)

print(response.choices[0].message.content)
""";

        // 这里生成 curl 的流式调用示例。
        var curlStreamExample = $$$"""
curl {{{baseUrl}}}/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer {{{apiKey}}}" \
  -d '{{
    "model": "gemini-2.5-flash",
    "messages": [
      {{"role": "user", "content": "Write a short poem"}}
    ],
    "stream": true
  }}'
""";

        // 这里生成 Python OpenAI SDK 的流式调用示例。
        var pythonStreamExample = $$$"""
from openai import OpenAI

client = OpenAI(
    api_key="{{{apiKey}}}",
    base_url="{{{baseUrl}}}/v1"
)

stream = client.chat.completions.create(
    model="gemini-2.5-flash",
    messages=[
        {"role": "user", "content": "Write a short poem"}
    ],
    stream=True
)

for chunk in stream:
    if chunk.choices[0].delta.content:
        print(chunk.choices[0].delta.content, end="")
""";

        // 这里返回前端展示说明和代码示例所需的完整数据。
        return new JsonObject
        {
            // 这里返回示例请求统一使用的基础地址。
            ["base_url"] = baseUrl,

            // 这里返回默认展示的 API Key。
            ["api_key"] = apiKey,

            // 这里返回当前配置中的 API Key 数量。
            ["api_keys_count"] = _appContextService.Settings.ApiKeys.Count,

            // 这里返回全部 API Key，供前端在说明页展示或复制。
            ["all_api_keys"] = new JsonArray(_appContextService.Settings.ApiKeys.Select(key => (JsonNode?)key).ToArray()),

            // 这里返回各种语言和调用模式的示例代码。
            ["examples"] = new JsonObject
            {
                // 这里返回 curl 的非流式示例。
                ["curl"] = curlExample,

                // 这里返回 curl 的流式示例。
                ["curl_stream"] = curlStreamExample,

                // 这里返回 Python requests 示例。
                ["python_requests"] = pythonRequestsExample,

                // 这里返回 Python OpenAI SDK 的非流式示例。
                ["python_openai"] = pythonOpenAiExample,

                // 这里返回 Python OpenAI SDK 的流式示例。
                ["python_stream"] = pythonStreamExample,
            },
        };
    }
}
