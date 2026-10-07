using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using xyRoslyn.Analyzers.Configuration;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    xyRoslyn.Analyzers.Configuration.EnvironmentQueryInBusinessCodeAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace xyRoslyn.Analyzers.Tests.Configuration;

public sealed class EnvironmentQueryInBusinessCodeAnalyzerTests
{
    // Minimal stand-ins for the hosting abstractions, the builder interfaces and IWebHostEnvironment.
    // The usings come first so that the test code appended after the stub can use them.
    private const string HostingStub = """
        using System;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;

        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostEnvironment
            {
                string EnvironmentName { get; set; }
                string ContentRootPath { get; set; }
            }

            public static class HostEnvironmentEnvExtensions
            {
                public static bool IsDevelopment(this IHostEnvironment hostEnvironment) => false;
                public static bool IsProduction(this IHostEnvironment hostEnvironment) => false;
                public static bool IsStaging(this IHostEnvironment hostEnvironment) => false;
                public static bool IsEnvironment(this IHostEnvironment hostEnvironment, string environmentName) => false;
            }

            public interface IHostApplicationBuilder { }
            public interface IHostBuilder { }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public interface IApplicationBuilder { }
        }

        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Hosting
        {
            public interface IWebHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
            {
                string WebRootPath { get; set; }
            }
        }

        public static class Startup
        {
            public static IHostEnvironment Environment => null!;
            public static void Configure(Func<bool> option) { }
        }

        """;

    [Theory]
    [InlineData("env.{|ROSA007:IsDevelopment|}()")]
    [InlineData("env.{|ROSA007:IsProduction|}()")]
    [InlineData("env.{|ROSA007:IsStaging|}()")]
    [InlineData("env.{|ROSA007:IsEnvironment|}(\"Staging\")")]
    public Task Reports_every_environment_check_in_a_service_method(string check) => Verify.VerifyAnalyzerAsync(HostingStub + $$"""
        public sealed class PriceService(IHostEnvironment env)
        {
            public decimal VatRate()
            {
                return {{check}} ? 0.19m : 0m;
            }
        }
        """);

    [Fact]
    public Task Reports_IsProduction_in_a_property_getter() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class PriceService(IHostEnvironment env)
        {
            public decimal VatRate => env.{|ROSA007:IsProduction|}() ? 0.19m : 0m;
        }
        """);

    [Fact]
    public Task Reports_an_environment_check_in_a_field_initializer() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class PriceService(IHostEnvironment env)
        {
            private readonly bool _verbose = env.{|ROSA007:IsDevelopment|}();

            public bool Verbose => _verbose;
        }
        """);

    [Fact]
    public Task Reports_a_read_of_EnvironmentName_in_a_service() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class PriceService(IHostEnvironment env)
        {
            public bool IsLive() => env.{|ROSA007:EnvironmentName|} == "Production";
        }
        """);

    [Fact]
    public Task Reports_IsDevelopment_on_IWebHostEnvironment() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class PriceService(IWebHostEnvironment env)
        {
            public bool Verbose() => env.{|ROSA007:IsDevelopment|}();
        }
        """);

    // EnvironmentName is declared on IHostEnvironment, also when read through the derived interface.
    [Fact]
    public Task Reports_a_read_of_EnvironmentName_through_IWebHostEnvironment() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class PriceService(IWebHostEnvironment env)
        {
            public bool IsLive() => env.{|ROSA007:EnvironmentName|} == "Production";
        }
        """);

    [Fact]
    public Task Reports_the_check_name_as_message_argument() => Verify.VerifyAnalyzerAsync(
        HostingStub + """
            public sealed class PriceService(IHostEnvironment env)
            {
                public decimal VatRate => env.{|#0:IsProduction|}() ? 0.19m : 0m;
            }
            """,
        Verify.Diagnostic().WithLocation(0).WithArguments("IsProduction"));

    [Fact]
    public Task Reports_EnvironmentName_as_message_argument() => Verify.VerifyAnalyzerAsync(
        HostingStub + """
            public sealed class PriceService(IHostEnvironment env)
            {
                public bool IsLive() => env.{|#0:EnvironmentName|} == "Production";
            }
            """,
        Verify.Diagnostic().WithLocation(0).WithArguments("EnvironmentName"));

    // Top-level statements must precede type declarations, so Program.cs is a second source.
    [Fact]
    public Task Does_not_report_in_top_level_statements() => new CSharpAnalyzerTest<EnvironmentQueryInBusinessCodeAnalyzer, DefaultVerifier>
    {
        TestState =
        {
            OutputKind = OutputKind.ConsoleApplication,
            Sources =
            {
                HostingStub,
                """
                using Microsoft.Extensions.Hosting;

                var env = Startup.Environment;
                if (env.IsDevelopment())
                {
                    System.Console.WriteLine(env.EnvironmentName);
                }
                """,
            },
        },
    }.RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Does_not_report_in_a_lambda_inside_top_level_statements() => new CSharpAnalyzerTest<EnvironmentQueryInBusinessCodeAnalyzer, DefaultVerifier>
    {
        TestState =
        {
            OutputKind = OutputKind.ConsoleApplication,
            Sources =
            {
                HostingStub,
                """
                using Microsoft.Extensions.Hosting;

                var env = Startup.Environment;
                Startup.Configure(() => env.IsDevelopment());
                """,
            },
        },
    }.RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Does_not_report_in_a_classic_static_Main() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public static class Program
        {
            public static void Main(string[] args)
            {
                var env = Startup.Environment;
                if (env.IsDevelopment())
                {
                    Console.WriteLine(env.EnvironmentName);
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_in_an_Add_extension_on_IServiceCollection() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public static class OrderingRegistration
        {
            public static IServiceCollection AddOrdering(this IServiceCollection services, IHostEnvironment env)
            {
                if (env.IsDevelopment() || env.EnvironmentName == "Local")
                {
                    Console.WriteLine("developer tools");
                }

                return services;
            }
        }
        """);

    [Theory]
    [InlineData("Microsoft.Extensions.Hosting.IHostApplicationBuilder")]
    [InlineData("Microsoft.Extensions.Hosting.IHostBuilder")]
    [InlineData("Microsoft.AspNetCore.Builder.IApplicationBuilder")]
    [InlineData("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder")]
    public Task Does_not_report_in_a_method_with_a_builder_parameter(string builderType) => Verify.VerifyAnalyzerAsync(HostingStub + $$"""
        public static class DeveloperTools
        {
            public static void UseDeveloperTools({{builderType}} builder, IHostEnvironment env)
            {
                if (env.IsDevelopment())
                {
                    Console.WriteLine(env.EnvironmentName);
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_with_a_parameter_of_a_type_implementing_a_builder_interface() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class WebApplication : Microsoft.AspNetCore.Builder.IApplicationBuilder, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder
        {
            public IWebHostEnvironment Environment => null!;
        }

        public static class DeveloperTools
        {
            public static void UseDeveloperTools(WebApplication app)
            {
                if (app.Environment.IsDevelopment())
                {
                    Console.WriteLine(app.Environment.EnvironmentName);
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_an_assignment_to_EnvironmentName() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class TestEnvironmentSetup(IHostEnvironment env)
        {
            public void UseStaging() => env.EnvironmentName = "Staging";
        }
        """);

    [Fact]
    public Task Does_not_report_a_read_of_ContentRootPath() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class TemplateStore(IHostEnvironment env)
        {
            public string TemplateDirectory() => System.IO.Path.Combine(env.ContentRootPath, "Templates");
        }
        """);

    [Fact]
    public Task Does_not_report_a_same_named_extension_on_a_foreign_class() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public static class MyEnvironmentExtensions
        {
            public static bool IsDevelopment(this IWebHostEnvironment env, bool local) => local;
        }

        public sealed class PriceService(IWebHostEnvironment env)
        {
            public bool Verbose() => env.IsDevelopment(true);
        }
        """);

    // Also covers a project without Microsoft.Extensions.Hosting: no diagnostic and no AD0001.
    [Fact]
    public Task Does_not_report_without_Microsoft_Extensions_Hosting() => Verify.VerifyAnalyzerAsync("""
        namespace Legacy.Hosting
        {
            public interface IHostEnvironment
            {
                string EnvironmentName { get; }
            }

            public static class HostEnvironmentEnvExtensions
            {
                public static bool IsDevelopment(this IHostEnvironment env) => false;
            }

            public sealed class PriceService(IHostEnvironment env)
            {
                public bool Verbose() => env.IsDevelopment() || env.EnvironmentName == "Local";
            }
        }
        """);

    // Incomplete code: the analyzer keeps working on a check whose receiver does not compile (no AD0001).
    [Fact]
    public Task Reports_an_environment_check_on_an_undeclared_receiver() => Verify.VerifyAnalyzerAsync(
        HostingStub + """
            public sealed class PriceService
            {
                public bool Verbose() => ((IHostEnvironment){|#0:missing|}).{|ROSA007:IsDevelopment|}();
            }
            """,
        DiagnosticResult.CompilerError("CS0103").WithLocation(0).WithArguments("missing"));

    // Against the real package, so the stubs above cannot drift from the real types unnoticed.
    [Fact]
    public Task Reports_IsDevelopment_against_the_real_hosting_package() => new CSharpAnalyzerTest<EnvironmentQueryInBusinessCodeAnalyzer, DefaultVerifier>
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100.AddPackages(
            [new PackageIdentity("Microsoft.Extensions.Hosting.Abstractions", "10.0.12")]),
        TestCode = """
            using Microsoft.Extensions.Hosting;

            public sealed class PriceService(IHostEnvironment env)
            {
                public decimal VatRate => env.{|ROSA007:IsDevelopment|}() ? 0m : 0.19m;
            }
            """,
    }.RunAsync(TestContext.Current.CancellationToken);

    // IServiceCollection comes from the real Microsoft.Extensions.DependencyInjection.Abstractions.
    [Fact]
    public Task Does_not_report_in_an_Add_extension_against_the_real_hosting_package() => new CSharpAnalyzerTest<EnvironmentQueryInBusinessCodeAnalyzer, DefaultVerifier>
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100.AddPackages(
            [new PackageIdentity("Microsoft.Extensions.Hosting.Abstractions", "10.0.12")]),
        TestCode = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;

            public static class OrderingRegistration
            {
                public static IServiceCollection AddOrdering(this IServiceCollection services, IHostEnvironment env)
                {
                    if (env.IsDevelopment())
                    {
                        System.Console.WriteLine(env.EnvironmentName);
                    }

                    return services;
                }
            }
            """,
    }.RunAsync(TestContext.Current.CancellationToken);
}
