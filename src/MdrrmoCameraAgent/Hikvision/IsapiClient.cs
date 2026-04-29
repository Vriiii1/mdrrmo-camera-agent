using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace MdrrmoCameraAgent.Hikvision;

public sealed class IsapiClient(HttpClient http, string user, string pass)
{
    public async Task<IReadOnlyList<HikvisionChannel>> ListChannelsAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "/ISAPI/ContentMgmt/InputProxy/channels");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var xml = await resp.Content.ReadAsStringAsync(ct);

        var doc = XDocument.Parse(xml);
        // Some firmware versions include a default XML namespace; resolve it dynamically.
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        return doc.Descendants(ns + "InputProxyChannel")
            .Select(el =>
            {
                var idStr = (string?)el.Element(ns + "id") ?? "0";
                var id    = int.TryParse(idStr, out var i) ? i : 0;
                var name  = (string?)el.Element(ns + "name");
                return new HikvisionChannel(id,
                    string.IsNullOrWhiteSpace(name) ? $"Channel {id}" : name);
            })
            .ToArray();
    }
}
