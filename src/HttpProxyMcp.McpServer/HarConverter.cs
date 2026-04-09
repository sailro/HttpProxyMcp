using System.Text.Json;
using System.Web;
using HttpProxyMcp.Core.Models;

namespace HttpProxyMcp.McpServer;

// Converts captured TrafficEntry data to HAR 1.2 JSON format.
public static class HarConverter
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		IndentSize = 2,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private static readonly HashSet<string> _textMimePatterns = new(StringComparer.OrdinalIgnoreCase)
	{
		"text/",
		"application/json",
		"application/xml",
		"application/javascript",
		"application/x-javascript",
		"application/ecmascript",
		"application/x-www-form-urlencoded",
		"application/xhtml+xml",
		"application/soap+xml",
		"application/graphql",
		"image/svg+xml"
	};

	public static string ConvertToHarJson(IReadOnlyList<TrafficEntry> entries)
	{
		var har = new Dictionary<string, object>
		{
			["log"] = new Dictionary<string, object>
			{
				["version"] = "1.2",
				["creator"] = new Dictionary<string, string>
				{
					["name"] = "HttpProxyMcp",
					["version"] = "1.0"
				},
				["entries"] = entries
					.OrderBy(e => e.StartedAt)
					.Select(ConvertEntry)
					.ToList()
			}
		};

		return JsonSerializer.Serialize(har, _jsonOptions);
	}

	private static Dictionary<string, object> ConvertEntry(TrafficEntry entry)
	{
		var durationMs = entry.Duration?.TotalMilliseconds ?? 0;

		var harEntry = new Dictionary<string, object>
		{
			["startedDateTime"] = entry.StartedAt.ToString("O"),
			["time"] = durationMs,
			["request"] = BuildRequest(entry.Request),
			["response"] = BuildResponse(entry.Response),
			["cache"] = new Dictionary<string, object>(),
			["timings"] = BuildTimings(entry, durationMs)
		};

		if (!string.IsNullOrEmpty(entry.ServerIpAddress))
			harEntry["serverIPAddress"] = entry.ServerIpAddress;

		harEntry["connection"] = "";

		return harEntry;
	}

	private static Dictionary<string, object> BuildRequest(CapturedRequest req)
	{
		var result = new Dictionary<string, object>
		{
			["method"] = req.Method,
			["url"] = req.Url,
			["httpVersion"] = req.HttpVersion ?? "HTTP/1.1",
			["cookies"] = ParseRequestCookies(req.Headers),
			["headers"] = FlattenHeaders(req.Headers),
			["queryString"] = ParseQueryString(req.QueryString),
			["headersSize"] = -1,
			["bodySize"] = req.ContentLength ?? -1
		};

		if (req.Body is not null)
			result["postData"] = BuildPostData(req.Body, req.ContentType);

		return result;
	}

	private static Dictionary<string, object> BuildResponse(CapturedResponse? res)
	{
		if (res is null)
		{
			return new Dictionary<string, object>
			{
				["status"] = 0,
				["statusText"] = "",
				["httpVersion"] = "HTTP/1.1",
				["cookies"] = new List<object>(),
				["headers"] = new List<object>(),
				["content"] = new Dictionary<string, object>
				{
					["size"] = 0,
					["mimeType"] = ""
				},
				["redirectURL"] = "",
				["headersSize"] = -1,
				["bodySize"] = -1
			};
		}

		var content = BuildResponseContent(res.Body, res.ContentType);

		return new Dictionary<string, object>
		{
			["status"] = res.StatusCode,
			["statusText"] = res.ReasonPhrase ?? "",
			["httpVersion"] = res.HttpVersion ?? "HTTP/1.1",
			["cookies"] = ParseResponseCookies(res.Headers),
			["headers"] = FlattenHeaders(res.Headers),
			["content"] = content,
			["redirectURL"] = GetHeaderValue(res.Headers, "Location") ?? "",
			["headersSize"] = -1,
			["bodySize"] = res.ContentLength ?? -1
		};
	}

	private static Dictionary<string, object> BuildTimings(TrafficEntry entry, double durationMs)
	{
		// Use actual timings when all three are populated
		if (entry.TimingSendMs.HasValue && entry.TimingWaitMs.HasValue && entry.TimingReceiveMs.HasValue)
		{
			return new Dictionary<string, object>
			{
				["send"] = entry.TimingSendMs.Value,
				["wait"] = entry.TimingWaitMs.Value,
				["receive"] = entry.TimingReceiveMs.Value
			};
		}

		// Estimate realistic distribution from total duration for better Chrome waterfall
		if (durationMs <= 0)
		{
			return new Dictionary<string, object>
			{
				["send"] = 0,
				["wait"] = 0,
				["receive"] = 0
			};
		}

		var send = Math.Min(durationMs * 0.01, 5.0);
		send = Math.Max(send, 0.5);

		// Estimate receive time based on response body size (~10 MB/s throughput)
		var responseBytes = entry.Response?.Body?.Length ?? 0;
		var receive = responseBytes > 0
			? Math.Max(0.5, responseBytes / 10_000.0)
			: 0.5;
		receive = Math.Min(receive, durationMs * 0.4);

		var wait = Math.Max(0, durationMs - send - receive);

		return new Dictionary<string, object>
		{
			["send"] = Math.Round(send, 3),
			["wait"] = Math.Round(wait, 3),
			["receive"] = Math.Round(receive, 3)
		};
	}

	private static Dictionary<string, object> BuildPostData(byte[] body, string? contentType)
	{
		var result = new Dictionary<string, object>
		{
			["mimeType"] = contentType ?? ""
		};

		if (IsTextContent(contentType))
		{
			result["text"] = System.Text.Encoding.UTF8.GetString(body);
		}
		else
		{
			result["text"] = Convert.ToBase64String(body);
			result["encoding"] = "base64";
		}

		return result;
	}

	private static Dictionary<string, object> BuildResponseContent(byte[]? body, string? contentType)
	{
		var content = new Dictionary<string, object>
		{
			["size"] = body?.Length ?? 0,
			["mimeType"] = contentType ?? ""
		};

		if (body is not null)
		{
			if (IsTextContent(contentType))
			{
				content["text"] = System.Text.Encoding.UTF8.GetString(body);
			}
			else
			{
				content["text"] = Convert.ToBase64String(body);
				content["encoding"] = "base64";
			}
		}

		return content;
	}

	private static List<Dictionary<string, string>> FlattenHeaders(Dictionary<string, string[]> headers)
	{
		var result = new List<Dictionary<string, string>>();
		foreach (var (name, values) in headers)
		{
			foreach (var value in values)
			{
				result.Add(new Dictionary<string, string>
				{
					["name"] = name,
					["value"] = value
				});
			}
		}
		return result;
	}

	private static List<Dictionary<string, string>> ParseQueryString(string? queryString)
	{
		var result = new List<Dictionary<string, string>>();
		if (string.IsNullOrEmpty(queryString)) return result;

		var qs = queryString.TrimStart('?');
		var parsed = HttpUtility.ParseQueryString(qs);

		foreach (string? key in parsed)
		{
			if (key is null) continue;
			var values = parsed.GetValues(key);
			if (values is null) continue;

			foreach (var value in values)
			{
				result.Add(new Dictionary<string, string>
				{
					["name"] = key,
					["value"] = value
				});
			}
		}

		return result;
	}

	private static List<Dictionary<string, string>> ParseRequestCookies(Dictionary<string, string[]> headers)
	{
		var result = new List<Dictionary<string, string>>();
		var cookieValues = GetHeaderValues(headers, "Cookie");

		foreach (var cookieHeader in cookieValues)
		{
			var pairs = cookieHeader.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			foreach (var pair in pairs)
			{
				var eqIndex = pair.IndexOf('=');
				if (eqIndex > 0)
				{
					result.Add(new Dictionary<string, string>
					{
						["name"] = pair[..eqIndex].Trim(),
						["value"] = pair[(eqIndex + 1)..].Trim()
					});
				}
			}
		}

		return result;
	}

	private static List<Dictionary<string, object>> ParseResponseCookies(Dictionary<string, string[]> headers)
	{
		var result = new List<Dictionary<string, object>>();
		var setCookieValues = GetHeaderValues(headers, "Set-Cookie");

		foreach (var setCookie in setCookieValues)
		{
			var parts = setCookie.Split(';', StringSplitOptions.TrimEntries);
			if (parts.Length == 0) continue;

			// First part is name=value
			var nameValue = parts[0];
			var eqIndex = nameValue.IndexOf('=');
			if (eqIndex <= 0) continue;

			var cookie = new Dictionary<string, object>
			{
				["name"] = nameValue[..eqIndex].Trim(),
				["value"] = nameValue[(eqIndex + 1)..].Trim()
			};

			// Parse attributes
			for (var i = 1; i < parts.Length; i++)
			{
				var attr = parts[i];
				var attrEq = attr.IndexOf('=');
				var attrName = attrEq > 0 ? attr[..attrEq].Trim() : attr.Trim();
				var attrValue = attrEq > 0 ? attr[(attrEq + 1)..].Trim() : "";

				if (attrName.Equals("Path", StringComparison.OrdinalIgnoreCase))
					cookie["path"] = attrValue;
				else if (attrName.Equals("Domain", StringComparison.OrdinalIgnoreCase))
					cookie["domain"] = attrValue;
				else if (attrName.Equals("Expires", StringComparison.OrdinalIgnoreCase))
					cookie["expires"] = attrValue;
				else if (attrName.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase))
					cookie["httpOnly"] = true;
				else if (attrName.Equals("Secure", StringComparison.OrdinalIgnoreCase))
					cookie["secure"] = true;
			}

			result.Add(cookie);
		}

		return result;
	}

	private static bool IsTextContent(string? contentType)
	{
		if (string.IsNullOrEmpty(contentType)) return false;

		foreach (var pattern in _textMimePatterns)
		{
			if (pattern.EndsWith('/'))
			{
				if (contentType.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			else
			{
				if (contentType.Contains(pattern, StringComparison.OrdinalIgnoreCase))
					return true;
			}
		}

		return false;
	}

	private static string? GetHeaderValue(Dictionary<string, string[]> headers, string name)
	{
		var match = headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
		return match.Value?.FirstOrDefault();
	}

	private static string[] GetHeaderValues(Dictionary<string, string[]> headers, string name)
	{
		var match = headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
		return match.Value ?? [];
	}
}
