/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Epic;
using AchievementCaretaker.Providers.Gog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AchievementCaretaker.Providers.Tests
{
    /// <summary>
    /// What the two <c>Add&lt;Store&gt;Provider</c> registrations promise to the application.
    /// </summary>
    public sealed class ProviderRegistrationTests : IDisposable
    {
        private readonly string _Home = Path.Combine(Path.GetTempPath(), "providers-" + Guid.NewGuid().ToString("N"));
        private readonly ServiceProvider _Services;

        public ProviderRegistrationTests()
        {
            Directory.CreateDirectory(this._Home);
            this._Services = new ServiceCollection()
                .AddEpicProvider(this._Home)
                .AddGogProvider(this._Home)
                .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        }

        public void Dispose()
        {
            this._Services.Dispose();
            try
            {
                Directory.Delete(this._Home, true);
            }
            catch (IOException)
            {
            }
        }

        private IStoreProvider Provider(string id) =>
            this._Services.GetServices<IStoreProvider>().Single(p => p.Id == id);

        [Fact]
        public void Both_providers_are_registered_as_IStoreProvider()
        {
            var ids = this._Services.GetServices<IStoreProvider>().Select(p => p.Id).OrderBy(id => id);

            Assert.Equal(new[] { "epic", "gog" }, ids);
        }

        [Fact]
        public void A_provider_is_one_instance_for_the_life_of_the_container()
        {
            Assert.Same(this.Provider("gog"), this.Provider("gog"));
        }

        [Fact]
        public void A_provider_with_no_session_file_is_signed_out()
        {
            foreach (var provider in this._Services.GetServices<IStoreProvider>())
            {
                Assert.False(provider.IsSignedIn);
                Assert.Null(provider.AccountId);
                Assert.Null(provider.AccountName);
            }
        }

        [Theory]
        [InlineData(EpicProvider.HttpClientName)]
        [InlineData(GogProvider.HttpClientName)]
        public void Each_provider_has_a_named_client_with_a_timeout_and_a_user_agent(string name)
        {
            var client = this._Services.GetRequiredService<IHttpClientFactory>().CreateClient(name);

            Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
            Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);
        }

        [Fact]
        public void The_factory_hands_out_a_new_client_each_time()
        {
            var factory = this._Services.GetRequiredService<IHttpClientFactory>();

            Assert.NotSame(factory.CreateClient(GogProvider.HttpClientName), factory.CreateClient(GogProvider.HttpClientName));
        }

        [Theory]
        [InlineData("gog", "https://embed.gog.com/on_login_success?origin=client&code=abcdef123456", SignInResultKind.Address)]
        [InlineData("gog", "https://embed.gog.com/on_login_success?origin=client", SignInResultKind.None)]
        [InlineData("gog", "https://auth.gog.com/auth?client_id=1", SignInResultKind.None)]
        [InlineData("epic", "https://www.epicgames.com/id/api/redirect?clientId=x&responseType=code", SignInResultKind.PageText)]
        [InlineData("epic", "https://www.epicgames.com/id/login", SignInResultKind.None)]
        [InlineData("epic", "https://example.com/id/api/redirect", SignInResultKind.None)]
        public void The_sign_in_page_says_when_it_has_finished(string id, string address, SignInResultKind expected)
        {
            Assert.Equal(expected, this.Provider(id).GetSignInResultKind(new Uri(address)));
        }

        [Fact]
        public async Task Signing_in_with_nonsense_is_refused_without_a_request()
        {
            var provider = this.Provider("gog");

            var error = await Assert.ThrowsAsync<ProviderException>(() => provider.SignInAsync("not a code", TestContext.Current.CancellationToken));

            Assert.Equal("invalid_code", error.ErrorCode);
        }

        [Fact]
        public void Only_GOG_can_change_achievements()
        {
            Assert.IsAssignableFrom<IWritableStoreProvider>(this.Provider("gog"));
            Assert.False(this.Provider("epic") is IWritableStoreProvider);
        }
    }
}
