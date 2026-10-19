/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AchievementCaretaker.Providers.Tests
{
    /// <summary>
    /// The session stores keep a credential for a whole store account, so what is on disk
    /// matters as much as what is read back.
    /// </summary>
    public sealed class SessionStoreTests : IDisposable
    {
        public sealed class Sample
        {
            public string RefreshToken { get; set; }
        }

        private const string Token = "refresh-token-value";

        private readonly string _Directory =
            Path.Combine(Path.GetTempPath(), "sessionstore-" + Guid.NewGuid().ToString("N"));

        public SessionStoreTests()
        {
            Directory.CreateDirectory(this._Directory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this._Directory, true);
            }
            catch (IOException)
            {
            }
        }

        private string PathOf(string name) => Path.Combine(this._Directory, name);

        /// <summary>The Windows store, or a skip: it cannot run anywhere else.</summary>
        private static ISessionStore Dpapi()
        {
            if (OperatingSystem.IsWindows())
            {
                return CreateDpapi();
            }

            Assert.Skip("DPAPI is Windows only.");
            return null;
        }

        [SupportedOSPlatform("windows")]
        private static ISessionStore CreateDpapi() => new DpapiSessionStore();

        /// <summary>The store for other systems, or a skip: it cannot run on Windows.</summary>
        private static ISessionStore Plain()
        {
            if (!OperatingSystem.IsWindows())
            {
                return CreatePlain();
            }

            Assert.Skip("The plain store is for Linux and macOS.");
            return null;
        }

        [UnsupportedOSPlatform("windows")]
        private static ISessionStore CreatePlain() => new PlainSessionStore();

        [Fact]
        public void AddSessionStore_picks_the_store_for_this_system()
        {
            using var provider = new ServiceCollection().AddSessionStore().BuildServiceProvider();

            var store = provider.GetRequiredService<ISessionStore>();

            Assert.IsType(OperatingSystem.IsWindows() ? typeof(DpapiSessionStore) : typeof(PlainSessionStore), store);
        }

        [Fact]
        public void AddSessionStore_can_be_called_by_every_provider()
        {
            var services = new ServiceCollection().AddSessionStore().AddSessionStore();

            Assert.Single(services, d => d.ServiceType == typeof(ISessionStore));
        }

        [Fact]
        public void Dpapi_round_trips_a_session()
        {
            var store = Dpapi();
            var path = this.PathOf("a.json");

            store.Save(path, new Sample { RefreshToken = Token });

            Assert.Equal(Token, store.Load<Sample>(path)?.RefreshToken);
        }

        [Fact]
        public void Dpapi_keeps_the_token_unreadable_on_disk()
        {
            var store = Dpapi();
            var path = this.PathOf("a.json");

            store.Save(path, new Sample { RefreshToken = Token });
            var onDisk = File.ReadAllBytes(path);

            Assert.NotEqual((byte)'{', onDisk[0]);
            Assert.DoesNotContain(Token, Encoding.UTF8.GetString(onDisk));
        }

        [Fact]
        public void Dpapi_reads_a_plain_file_written_by_an_older_version_and_encrypts_it()
        {
            var store = Dpapi();
            var path = this.PathOf("old.json");
            File.WriteAllText(path, "{ \"RefreshToken\": \"" + Token + "\" }");

            var loaded = store.Load<Sample>(path);

            Assert.Equal(Token, loaded?.RefreshToken);
            var onDisk = File.ReadAllBytes(path);
            Assert.NotEqual((byte)'{', onDisk[0]);
            Assert.DoesNotContain(Token, Encoding.UTF8.GetString(onDisk));
            Assert.Equal(Token, store.Load<Sample>(path)?.RefreshToken);
        }

        [Fact]
        public void Dpapi_reads_plain_json_behind_a_byte_order_mark()
        {
            var store = Dpapi();
            var path = this.PathOf("bom.json");
            var json = Encoding.UTF8.GetBytes("  { \"RefreshToken\": \"" + Token + "\" }");
            File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(json).ToArray());

            Assert.Equal(Token, store.Load<Sample>(path)?.RefreshToken);
        }

        [Fact]
        public void Dpapi_treats_a_corrupt_file_as_no_session()
        {
            var store = Dpapi();
            var path = this.PathOf("junk.json");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            Assert.Null(store.Load<Sample>(path));
        }

        [Fact]
        public void Dpapi_leaves_a_file_it_could_not_read_as_it_was()
        {
            var store = Dpapi();
            var path = this.PathOf("broken.json");
            File.WriteAllText(path, "{ not json");

            Assert.Null(store.Load<Sample>(path));
            Assert.Equal("{ not json", File.ReadAllText(path));
        }

        [Fact]
        public void Dpapi_returns_null_for_a_missing_file()
        {
            Assert.Null(Dpapi().Load<Sample>(this.PathOf("missing.json")));
        }

        [Fact]
        public void Dpapi_deletes_a_session_and_tolerates_a_missing_one()
        {
            var store = Dpapi();
            var path = this.PathOf("a.json");
            store.Save(path, new Sample { RefreshToken = Token });

            store.Delete(path);
            store.Delete(path);

            Assert.False(File.Exists(path));
        }

        [Fact]
        [UnsupportedOSPlatform("windows")]
        public void Plain_round_trips_a_session_readable_by_the_user_only()
        {
            var store = Plain();
            var path = this.PathOf("a.json");

            store.Save(path, new Sample { RefreshToken = Token });

            Assert.Equal(Token, store.Load<Sample>(path)?.RefreshToken);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }

        [Fact]
        public void Plain_treats_a_corrupt_file_as_no_session()
        {
            var store = Plain();
            var path = this.PathOf("broken.json");
            File.WriteAllText(path, "{ not json");

            Assert.Null(store.Load<Sample>(path));
        }
    }
}
