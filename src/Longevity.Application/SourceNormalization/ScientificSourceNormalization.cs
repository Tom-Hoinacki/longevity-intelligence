using System.Security.Cryptography;
using System.Text;

namespace Longevity.Application.SourceNormalization;

public sealed record AuthoritativeSourceIdentifiers(string? Doi = null, string? Pmid = null, string? ClinicalTrialsGovIdentifier = null, string? CanonicalUrl = null);

public sealed record ScientificSourceNormalizationRequest(string SourceType, string Title, string RawContent, AuthoritativeSourceIdentifiers? Identifiers = null)
{
    public AuthoritativeSourceIdentifiers Identifiers { get; } = Identifiers ?? new();
    public string SourceType { get; } = Require(SourceType, nameof(SourceType));
    public string Title { get; } = Require(Title, nameof(Title));
    public string RawContent { get; } = RawContent ?? throw new ArgumentNullException(nameof(RawContent));
    private static string Require(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("The field must be non-empty.", name) : value.Trim();
}

public sealed record ScientificSourceNormalizationResult(string SourceType, string Title, string NormalizedText, string SourceIdentityKey, string? CanonicalUrl, string ContentHash, string NormalizationVersion);
public interface IScientificSourceNormalizer { ScientificSourceNormalizationResult Normalize(ScientificSourceNormalizationRequest request); }

public sealed class ScientificSourceNormalizer : IScientificSourceNormalizer
{
    public const string Version = "scientific-source-v1";
    public ScientificSourceNormalizationResult Normalize(ScientificSourceNormalizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var title = Text(request.Title, nameof(request.Title)).Trim();
        var text = Text(request.RawContent, nameof(request.RawContent));
        var identity = Identity(request.Identifiers);
        var type = Text(request.SourceType, nameof(request.SourceType)).Trim();
        var url = Url(request.Identifiers.CanonicalUrl, false);
        var payload = string.Join("\n", Version, type, title, identity, url ?? "", text);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return new(type, title, text, identity, url, hash, Version);
    }

    private static string Identity(AuthoritativeSourceIdentifiers ids)
    {
        var doi = Doi(ids.Doi); if (doi is not null) return $"doi:{doi}";
        var pmid = Pmid(ids.Pmid); if (pmid is not null) return $"pmid:{pmid}";
        var nct = Nct(ids.ClinicalTrialsGovIdentifier); if (nct is not null) return $"clinicaltrials:{nct}";
        return $"url:{Url(ids.CanonicalUrl, true)}";
    }
    private static string? Doi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null; var v = value.Trim();
        if (v.StartsWith("doi:", StringComparison.OrdinalIgnoreCase)) v = v[4..];
        foreach (var p in new[] { "https://doi.org/", "http://doi.org/" }) if (v.StartsWith(p, StringComparison.OrdinalIgnoreCase)) v = v[p.Length..];
        v = v.Trim().ToLowerInvariant();
        if (v.Length > 512 || !v.StartsWith("10.", StringComparison.Ordinal) || !v.Contains('/')) throw new ArgumentException("The DOI must have a plausible 10.xxxx/identifier form.", "Doi");
        return v;
    }
    private static string? Pmid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null; var v = value.Trim();
        if (v.Length > 32 || v.Any(c => c is < '0' or > '9') || v.Trim('0').Length == 0) throw new ArgumentException("The PMID must contain digits and be non-zero.", "Pmid"); return v;
    }
    private static string? Nct(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null; var v = value.Trim().ToUpperInvariant();
        if (v.Length != 11 || !v.StartsWith("NCT", StringComparison.Ordinal) || v[3..].Any(c => c is < '0' or > '9')) throw new ArgumentException("The ClinicalTrials.gov identifier must be NCT followed by eight digits.", "ClinicalTrialsGovIdentifier"); return v;
    }
    private static string? Url(string? value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value)) { if (required) throw new ArgumentException("At least one authoritative source identifier is required.", "CanonicalUrl"); return null; }
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || uri.Host.Length == 0) throw new ArgumentException("The canonical URL must be an absolute HTTP or HTTPS URL.", "CanonicalUrl");
        var b = new UriBuilder(uri) { Fragment = "" }; if ((b.Scheme == "http" && b.Port == 80) || (b.Scheme == "https" && b.Port == 443)) b.Port = -1; if (string.IsNullOrEmpty(b.Path)) b.Path = "/"; return b.Uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
    }
    private static string Text(string value, string name)
    {
        if (value is null) throw new ArgumentNullException(name); var v = value.Normalize(NormalizationForm.FormC).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00A0', ' ');
        var lines = v.Split('\n').Select(x => new string(x.Where(c => c == '\t' || !char.IsControl(c)).ToArray()).TrimEnd(' ', '\t')).ToList(); while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0); while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        var sb = new StringBuilder(); var blanks = 0; foreach (var line in lines) { if (line.Length == 0 && ++blanks > 2) continue; if (line.Length > 0) blanks = 0; if (sb.Length > 0) sb.Append('\n'); sb.Append(line); }
        var result = sb.ToString(); if (result.Length == 0) throw new ArgumentException("The field must contain non-whitespace content.", name); return result;
    }
}
