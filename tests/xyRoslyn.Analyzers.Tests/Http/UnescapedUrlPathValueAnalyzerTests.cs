using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using xyRoslyn.Analyzers.Http;
using xyRoslyn.CodeFixes.Http;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    xyRoslyn.Analyzers.Http.UnescapedUrlPathValueAnalyzer, xyRoslyn.CodeFixes.Http.UnescapedUrlPathValueCodeFix,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace xyRoslyn.Analyzers.Tests.Http;

// Every case compiles against the real .NET 10 reference assemblies: HttpClient, HttpRequestMessage,
// Uri and System.Net.Http.Json are all in them, and the fix depends on their real nullable annotations.
// Markup inside an interpolated string is written {{|ROSA002:x|}}: the outer braces are the hole.
public sealed class UnescapedUrlPathValueAnalyzerTests
{
    // The usings come first so that the test code appended after them can use them.
    private const string Usings = """
        #nullable enable
        using System;
        using System.Net.Http;
        using System.Net.Http.Json;
        using System.Threading;
        using System.Threading.Tasks;

        """;

    // Cases with a diagnostic: also verifies the fix, FixAll and that the fixed code is clean.
    // fixedSource == source means the fix must not be offered.
    private static Task VerifyFixAsync(string source, string fixedSource, params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<UnescapedUrlPathValueAnalyzer, UnescapedUrlPathValueCodeFix, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            TestCode = source,
            FixedCode = fixedSource,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    // Cases without a diagnostic.
    private static Task VerifyAnalyzerAsync(string source)
        => new CSharpAnalyzerTest<UnescapedUrlPathValueAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);

    // The example of docs/analyzers/ROSA002.md: an ID "a/b" requests shipments/a/b.
    [Fact]
    public Task Wraps_a_variable_string_path_segment_in_EscapeDataString() => VerifyFixAsync(
        Usings + """
            public sealed class ShipmentClient
            {
                public async Task LoadAsync(HttpClient http, string shipmentId, CancellationToken ct)
                {
                    using var response = await http.GetAsync($"shipments/{{|ROSA002:shipmentId|}}", ct);
                }
            }
            """,
        Usings + """
            public sealed class ShipmentClient
            {
                public async Task LoadAsync(HttpClient http, string shipmentId, CancellationToken ct)
                {
                    using var response = await http.GetAsync($"shipments/{Uri.EscapeDataString(shipmentId)}", ct);
                }
            }
            """);

    [Fact]
    public Task Reports_the_hole_text_as_message_argument() => VerifyFixAsync(
        Usings + """
            public sealed record Shipment(string Id);

            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, Shipment shipment) => _ = http.GetAsync($"shipments/{{|#0:shipment.Id|}}");
            }
            """,
        Usings + """
            public sealed record Shipment(string Id);

            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, Shipment shipment) => _ = http.GetAsync($"shipments/{Uri.EscapeDataString(shipment.Id)}");
            }
            """,
        Verify.Diagnostic().WithLocation(0).WithArguments("shipment.Id"));

    // One row per kind of target: JSON extension, HttpRequestMessage, and new Uri(...) around the string.
    [Theory]
    [InlineData(
        "http.GetFromJsonAsync<int>($\"shipments/{{|ROSA002:shipmentId|}}\")",
        "http.GetFromJsonAsync<int>($\"shipments/{Uri.EscapeDataString(shipmentId)}\")")]
    [InlineData(
        "new HttpRequestMessage(HttpMethod.Get, $\"shipments/{{|ROSA002:shipmentId|}}\")",
        "new HttpRequestMessage(HttpMethod.Get, $\"shipments/{Uri.EscapeDataString(shipmentId)}\")")]
    [InlineData(
        "http.GetAsync(new Uri($\"shipments/{{|ROSA002:shipmentId|}}\", UriKind.Relative))",
        "http.GetAsync(new Uri($\"shipments/{Uri.EscapeDataString(shipmentId)}\", UriKind.Relative))")]
    [InlineData(
        "new HttpRequestMessage(HttpMethod.Get, new Uri($\"shipments/{{|ROSA002:shipmentId|}}\", UriKind.Relative))",
        "new HttpRequestMessage(HttpMethod.Get, new Uri($\"shipments/{Uri.EscapeDataString(shipmentId)}\", UriKind.Relative))")]
    public Task Reports_every_kind_of_request_target(string call, string fixedCall) => VerifyFixAsync(
        Usings + $$"""
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId) => _ = {{call}};
            }
            """,
        Usings + $$"""
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId) => _ = {{fixedCall}};
            }
            """);

    // Relative to a base Uri, the leading hole is a path segment, not a base address.
    [Fact]
    public Task Reports_the_leading_hole_of_a_path_relative_to_a_base_Uri() => VerifyFixAsync(
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, Uri baseUri, string shipmentId)
                    => _ = http.GetAsync(new Uri(baseUri, $"{{|ROSA002:shipmentId|}}/labels"));
            }
            """,
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, Uri baseUri, string shipmentId)
                    => _ = http.GetAsync(new Uri(baseUri, $"{Uri.EscapeDataString(shipmentId)}/labels"));
            }
            """);

    [Fact]
    public Task Reports_the_path_hole_before_a_query() => VerifyFixAsync(
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId, string expand)
                    => _ = http.GetAsync($"shipments/{{|ROSA002:shipmentId|}}?expand={expand}");
            }
            """,
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId, string expand)
                    => _ = http.GetAsync($"shipments/{Uri.EscapeDataString(shipmentId)}?expand={expand}");
            }
            """);

    // A date can format as 10/7/2026; EscapeDataString takes a string, so the fix is not offered.
    [Fact]
    public Task Reports_a_DateTime_value_without_offering_a_fix() => VerifyFixAsync(
        Usings + """
            public sealed class ReportClient
            {
                public void Load(HttpClient http, DateTime day) => _ = http.GetAsync($"reports/{{|ROSA002:day|}}");
            }
            """,
        Usings + """
            public sealed class ReportClient
            {
                public void Load(HttpClient http, DateTime day) => _ = http.GetAsync($"reports/{{|ROSA002:day|}}");
            }
            """);

    [Fact]
    public Task Reports_an_own_method_named_EscapeDataString() => VerifyFixAsync(
        Usings + """
            public static class Text
            {
                public static string EscapeDataString(string value) => value;
            }

            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId)
                    => _ = http.GetAsync($"shipments/{{|ROSA002:Text.EscapeDataString(shipmentId)|}}");
            }
            """,
        Usings + """
            public static class Text
            {
                public static string EscapeDataString(string value) => value;
            }

            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId)
                    => _ = http.GetAsync($"shipments/{Uri.EscapeDataString(Text.EscapeDataString(shipmentId))}");
            }
            """);

    // Incomplete code: the analyzer keeps working on a hole that does not bind (no AD0001).
    [Fact]
    public Task Reports_a_hole_with_an_undeclared_name_without_crashing() => VerifyFixAsync(
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http) => _ = http.GetAsync($"shipments/{{|#0:missing|}}");
            }
            """,
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http) => _ = http.GetAsync($"shipments/{{|#0:missing|}}");
            }
            """,
        DiagnosticResult.CompilerError("CS0103").WithLocation(0).WithArguments("missing"),
        Verify.Diagnostic().WithLocation(0).WithArguments("missing"));

    // Escaped values, integers, Guids (also nullable) and constants cannot contain '/', '?' or '#'.
    [Theory]
    [InlineData("{Uri.EscapeDataString(shipmentId)}")]
    [InlineData("{orderNumber}")]
    [InlineData("{batch}")]
    [InlineData("{trackingId}")]
    [InlineData("{correlationId}")]
    [InlineData("{Version}")]
    public Task Does_not_report_values_that_cannot_change_the_path(string hole) => VerifyAnalyzerAsync(Usings + $$"""
        public sealed class ShipmentClient
        {
            private const string Version = "v2";

            public void Load(HttpClient http, string shipmentId, int orderNumber, long? batch, Guid trackingId, Guid? correlationId)
                => _ = http.GetAsync($"shipments/{{hole}}");
        }
        """);

    // A leading hole is a base address (http-clients HTTP-003), not a path segment.
    [Fact]
    public Task Does_not_report_a_hole_at_the_start() => VerifyAnalyzerAsync(Usings + """
        public sealed class HealthClient
        {
            public void Check(HttpClient http, string baseUrl) => _ = http.GetAsync($"{baseUrl}/health");
        }
        """);

    [Theory]
    [InlineData("$\"shipments?id={shipmentId}\"")]
    [InlineData("$\"shipments#{shipmentId}\"")]
    public Task Does_not_report_holes_in_the_query_or_fragment(string url) => VerifyAnalyzerAsync(Usings + $$"""
        public sealed class ShipmentClient
        {
            public void Load(HttpClient http, string shipmentId) => _ = http.GetAsync({{url}});
        }
        """);

    [Fact]
    public Task Does_not_report_an_interpolated_request_body() => VerifyAnalyzerAsync(Usings + """
        public sealed class NoteClient
        {
            public void Add(HttpClient http, string note) => _ = http.PostAsJsonAsync("notes", $"note/{note}");
        }
        """);

    [Fact]
    public Task Does_not_report_a_same_named_HttpClient_in_another_namespace() => VerifyAnalyzerAsync(Usings + """
        namespace Legacy.Net
        {
            public class HttpClient
            {
                public Task GetAsync(string requestUri) => Task.CompletedTask;
            }

            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId) => _ = http.GetAsync($"shipments/{shipmentId}");
            }
        }
        """);

    // Documented limits (ROSA002.md "Limits"): if one of these starts to report, update the documentation.
    [Theory]
    [InlineData("var url = $\"shipments/{shipmentId}\"; _ = http.GetAsync(url);")]
    [InlineData("_ = http.GetAsync(\"shipments/\" + shipmentId);")]
    public Task Does_not_report_the_documented_limits(string body) => VerifyAnalyzerAsync(Usings + $$"""
        public sealed class ShipmentClient
        {
            public void Load(HttpClient http, string shipmentId)
            {
                {{body}}
            }
        }
        """);

    // Two holes in one string: the framework also verifies "fix all occurrences" with the BatchFixer.
    [Fact]
    public Task Fix_escapes_both_holes_of_one_url_with_fix_all() => VerifyFixAsync(
        Usings + """
            public sealed class OrderClient
            {
                public void Load(HttpClient http, string customerId, string orderId)
                    => _ = http.GetAsync($"customers/{{|ROSA002:customerId|}}/orders/{{|ROSA002:orderId|}}");
            }
            """,
        Usings + """
            public sealed class OrderClient
            {
                public void Load(HttpClient http, string customerId, string orderId)
                    => _ = http.GetAsync($"customers/{Uri.EscapeDataString(customerId)}/orders/{Uri.EscapeDataString(orderId)}");
            }
            """);

    [Fact]
    public Task Fix_qualifies_Uri_when_System_is_not_imported() => VerifyFixAsync(
        """
        #nullable enable
        using System.Net.Http;

        public sealed class ShipmentClient
        {
            public void Load(HttpClient http, string shipmentId) => _ = http.GetAsync($"shipments/{{|ROSA002:shipmentId|}}");
        }
        """,
        """
        #nullable enable
        using System.Net.Http;

        public sealed class ShipmentClient
        {
            public void Load(HttpClient http, string shipmentId) => _ = http.GetAsync($"shipments/{System.Uri.EscapeDataString(shipmentId)}");
        }
        """);

    [Fact]
    public Task Fix_keeps_the_spacing_and_comment_inside_the_hole() => VerifyFixAsync(
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId)
                    => _ = http.GetAsync($"shipments/{ {|ROSA002:shipmentId|} /* external */ }");
            }
            """,
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId)
                    => _ = http.GetAsync($"shipments/{ Uri.EscapeDataString(shipmentId) /* external */ }");
            }
            """);

    // Uri.EscapeDataString(null) throws, so a maybe-null string is fixed by hand.
    [Fact]
    public Task Fix_is_not_offered_for_a_maybe_null_string() => VerifyFixAsync(
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string? shipmentId) => _ = http.GetAsync($"shipments/{{|ROSA002:shipmentId|}}");
            }
            """,
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string? shipmentId) => _ = http.GetAsync($"shipments/{{|ROSA002:shipmentId|}}");
            }
            """);

    // The flow state counts, not the declared type: after the null check the value is known not to be null.
    [Fact]
    public Task Fix_is_offered_after_a_null_check() => VerifyFixAsync(
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string? shipmentId)
                {
                    ArgumentNullException.ThrowIfNull(shipmentId);
                    _ = http.GetAsync($"shipments/{{|ROSA002:shipmentId|}}");
                }
            }
            """,
        Usings + """
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string? shipmentId)
                {
                    ArgumentNullException.ThrowIfNull(shipmentId);
                    _ = http.GetAsync($"shipments/{Uri.EscapeDataString(shipmentId)}");
                }
            }
            """);

    [Theory]
    [InlineData("{{|ROSA002:shipmentId|},10}")]
    [InlineData("{{|ROSA002:shipmentId|}:x}")]
    public Task Fix_is_not_offered_with_alignment_or_format(string hole)
    {
        var source = Usings + $$"""
            public sealed class ShipmentClient
            {
                public void Load(HttpClient http, string shipmentId) => _ = http.GetAsync($"shipments/{{hole}}");
            }
            """;
        return VerifyFixAsync(source, source);
    }

    // Without nullable annotations the compiler does not know the string is not null: no fix.
    [Fact]
    public Task Fix_is_not_offered_in_a_nullable_disabled_context() => VerifyFixAsync(
        """
        using System.Net.Http;

        public sealed class ShipmentClient
        {
            public void Load(HttpClient http, string shipmentId) => _ = http.GetAsync($"shipments/{{|ROSA002:shipmentId|}}");
        }
        """,
        """
        using System.Net.Http;

        public sealed class ShipmentClient
        {
            public void Load(HttpClient http, string shipmentId) => _ = http.GetAsync($"shipments/{{|ROSA002:shipmentId|}}");
        }
        """);
}
