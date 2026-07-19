using Longevity.Application.SourceNormalization;

namespace Longevity.UnitTests.SourceNormalization;

public sealed class ScientificSourceNormalizerTests
{
    private static ScientificSourceNormalizationRequest Request(AuthoritativeSourceIdentifiers? ids = null, string text = "A\nB") => new("journal_article", " Title ", text, ids ?? new(Doi: " DOI:10.1234/ABC "));
    [Fact] public void Doi_is_normalized_and_has_priority() { var r = new ScientificSourceNormalizer().Normalize(Request(new(Doi: "https://doi.org/10.1234/ABC", Pmid: "42", CanonicalUrl: "https://example.org/x"))); Assert.Equal("doi:10.1234/abc", r.SourceIdentityKey); }
    [Fact] public void Pmid_and_nct_are_normalized() { var n = new ScientificSourceNormalizer(); Assert.Equal("pmid:42", n.Normalize(Request(new(Pmid: " 42 "))).SourceIdentityKey); Assert.Equal("clinicaltrials:nct12345678", n.Normalize(Request(new(ClinicalTrialsGovIdentifier: "nct12345678"))).SourceIdentityKey); }
    [Fact] public void Url_removes_fragment_and_default_port() { var r = new ScientificSourceNormalizer().Normalize(Request(new(CanonicalUrl: "HTTPS://Example.ORG:443/path?q=1#fragment"))); Assert.Equal("url:https://example.org/path?q=1", r.SourceIdentityKey); }
    [Fact] public void Invalid_identity_or_empty_content_is_rejected() { Assert.Throws<ArgumentException>(() => new ScientificSourceNormalizer().Normalize(Request(new(Pmid: "0")))); Assert.Throws<ArgumentException>(() => new ScientificSourceNormalizer().Normalize(new("journal_article", "title", " ", new(CanonicalUrl: "https://x")))); }
    [Fact] public void Text_rules_preserve_scientific_content() { var r = new ScientificSourceNormalizer().Normalize(new("journal_article", "\uFEFF T\u0301 ", "a\r\nb\r\n\r\n\r\n\r\n\tc\u00A0\u0001", new(CanonicalUrl: "https://x"))); Assert.Equal("T́", r.Title); Assert.Equal("a\nb\n\n\n\tc", r.NormalizedText); }
    [Fact] public void Unsupported_source_type_is_rejected() => Assert.Throws<ArgumentException>(() => new ScientificSourceNormalizationRequest("web_page", "title", "content", new(CanonicalUrl: "https://example.test")));
    [Fact] public void Line_endings_are_equivalent_and_hash_is_deterministic() { var n = new ScientificSourceNormalizer(); var a = n.Normalize(Request(text: "one\ntwo")); var b = n.Normalize(Request(text: "one\r\ntwo")); var c = n.Normalize(Request(text: "one\ntres")); Assert.Equal(a.ContentHash, b.ContentHash); Assert.NotEqual(a.ContentHash, c.ContentHash); Assert.Equal(64, a.ContentHash.Length); }
    [Fact] public void Invalid_url_schemes_and_sensitive_content_are_not_echoed() { var ex = Assert.Throws<ArgumentException>(() => new ScientificSourceNormalizer().Normalize(new("x", "title", "SECRET SCIENTIFIC TEXT", new(CanonicalUrl: "ftp://example.org")))); Assert.DoesNotContain("SECRET", ex.Message); }
}
