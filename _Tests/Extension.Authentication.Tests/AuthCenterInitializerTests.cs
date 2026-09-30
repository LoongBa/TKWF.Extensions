using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>D14：Initializer——[TKWFExtension] 特性 / DI 描述符 / TryAdd 不覆盖 / 零 DataService 手动注册。</summary>
public class AuthCenterInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(AuthCenterExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Authentication", attr!.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_ServiceFacades()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(ITokenService)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(IAuthLoginAttemptService)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(IOAuthTicketService)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(ISmsVerificationService)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(IPlatformCredentialService)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(IPlatformAccountMapService)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(IWeChatApiClient)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(ITokenVerifier)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(IAuthAccountQueryService)));
        Assert.NotNull(services.FirstOrDefault(d => d.ServiceType == typeof(IUserProfileSource)));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IAuthenticationProvider)));
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumer()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITokenService, ConsumerTokenService>();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(ITokenService)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerTokenService), descriptors[0].ImplementationType);
    }

    /// <summary>⚠️ D14 核心验收：零 DataService 手动注册（ADR61 铁律）——Initializer 不得 TryAddScoped 任何 *EntityDataService。</summary>
    [Fact]
    public void ConfigureServices_ZeroDataServiceManualRegistration()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var dataServiceDescriptors = services
            .Where(d => d.ServiceType.Name.EndsWith("DataService", System.StringComparison.Ordinal))
            .ToList();
        Assert.Empty(dataServiceDescriptors);
    }

    private sealed class ConsumerTokenService : ITokenService
    {
        public System.Threading.Tasks.Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task<TokenValidationResult> ValidateTokenAsync(string accessToken, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task<TokenRefreshResult> RefreshTokenAsync(string refreshToken, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task RevokeTokenAsync(string jti, string reason, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
    }
}
