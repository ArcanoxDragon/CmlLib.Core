using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CmlLib.Core.Auth
{
    public enum MLoginResult { Success, BadRequest, WrongAccount, NeedLogin, UnknownError, NoProfile }

    public class MLogin
    {
        public static readonly string DefaultLoginSessionFile = Path.Combine(MinecraftPath.GetOSDefaultPath(), "logintoken.json");

        public MLogin() : this(DefaultLoginSessionFile) { }

        public MLogin(string sessionCacheFilePath)
        {
            SessionCacheFilePath = sessionCacheFilePath;
        }

        public string SessionCacheFilePath { get; private set; }
        public bool SaveSession { get; private set; } = true;

        private string CreateNewClientToken()
        {
            return Guid.NewGuid().ToString().Replace("-", "");
        }

        private MSession CreateNewSession()
        {
            var session = new MSession();
            if (SaveSession)
            {
                session.ClientToken = CreateNewClientToken();
                WriteSessionCache(session);
            }
            return session;
        }

        private void WriteSessionCache(MSession session)
        {
            if (!SaveSession) return;
            Directory.CreateDirectory(Path.GetDirectoryName(SessionCacheFilePath));

            var json = JsonConvert.SerializeObject(session);
            File.WriteAllText(SessionCacheFilePath, json, Encoding.UTF8);
        }

        public MSession ReadSessionCache()
        {
            if (File.Exists(SessionCacheFilePath))
            {
                var filedata = File.ReadAllText(SessionCacheFilePath, Encoding.UTF8);
                try
                {
                    var session = JsonConvert.DeserializeObject<MSession>(filedata, new JsonSerializerSettings()
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    });

					if ( SaveSession && session != null && string.IsNullOrEmpty( session.ClientToken ) )
                        session.ClientToken = CreateNewClientToken();

                    return session;
                }
                catch (JsonReaderException) // invalid json
                {
                    return CreateNewSession();
                }
            }
            else
            {
                return CreateNewSession();
            }
        }

        private async Task<HttpResponseMessage> mojangRequestAsync(string endpoint, string postdata, CancellationToken cancellationToken = default)
		{
			using var http = new HttpClient();
            using var content = new StringContent( postdata, Encoding.UTF8, "application/json" );
			
			return await http.PostAsync( MojangServer.Auth + endpoint, content, cancellationToken );
        }

        private MLoginResponse parseSession(string json, string clientToken)
        {
            var job = JObject.Parse(json); //json parse

            var profile = job["selectedProfile"];
            if (profile == null)
                return new MLoginResponse(MLoginResult.NoProfile, null, null, json);
            else
            {
                var session = new MSession()
                {
                    AccessToken = job["accessToken"]?.ToString(),
                    UUID = profile["id"]?.ToString(),
                    Username = profile["name"]?.ToString(),
                    ClientToken = clientToken
                };

                WriteSessionCache(session);
                return new MLoginResponse(MLoginResult.Success, session, null, null);
            }
        }

        private MLoginResponse errorHandle(string json)
        {
            try
            {
                var job = JObject.Parse(json);

                var error = job["error"]?.ToString(); // error type
                var errormsg = job["message"]?.ToString() ?? ""; // detail error message
                MLoginResult result;

                switch (error)
                {
                    case "Method Not Allowed":
                    case "Not Found":
                    case "Unsupported Media Type":
                        result = MLoginResult.BadRequest;
                        break;
                    case "IllegalArgumentException":
                    case "ForbiddenOperationException":
                        result = MLoginResult.WrongAccount;
                        break;
                    default:
                        result = MLoginResult.UnknownError;
                        break;
                }

                return new MLoginResponse(result, null, errormsg, json);
            }
            catch (Exception ex)
            {
                return new MLoginResponse(MLoginResult.UnknownError, null, ex.ToString(), json);
            }
        }

        public Task<MLoginResponse> AuthenticateAsync(string id, string pw, CancellationToken cancellationToken = default)
        {
            var clientToken = ReadSessionCache().ClientToken;
            return AuthenticateAsync(id, pw, clientToken, cancellationToken);
        }

        public async Task<MLoginResponse> AuthenticateAsync(string id, string pw, string clientToken, CancellationToken cancellationToken = default)
        {
            var req = new JObject
            {
                { "username", id },
                { "password", pw },
                { "clientToken", clientToken },
                { "agent", new JObject
                    {
                        { "name", "Minecraft" },
                        { "version", 1 }
                    }
                }
            };

            var       response    = await mojangRequestAsync("authenticate", req.ToString(), cancellationToken);
			using var resStream   = await response.Content.ReadAsStreamAsync();
			using var res         = new StreamReader(resStream);
			var       rawResponse = await res.ReadToEndAsync();

			if (response.StatusCode == HttpStatusCode.OK) // ResultCode == 200
				return this.parseSession(rawResponse, clientToken);
			else // fail to login
				return this.errorHandle(rawResponse);
		}

        public Task<MLoginResponse> TryAutoLoginAsync(CancellationToken cancellationToken = default)
        {
            var session = ReadSessionCache();
            return TryAutoLoginAsync(session, cancellationToken);
        }

        public async Task<MLoginResponse> TryAutoLoginAsync(MSession session, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ValidateAsync(session, cancellationToken );
                if (result.Result != MLoginResult.Success)
                    result = await RefreshAsync(session, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                return new MLoginResponse(MLoginResult.UnknownError, null, ex.ToString(), null);
            }
        }

        public Task<MLoginResponse> RefreshAsync(CancellationToken cancellationToken = default )
        {
            var session = ReadSessionCache();
            return RefreshAsync(session, cancellationToken);
        }

        public async Task<MLoginResponse> RefreshAsync(MSession session, CancellationToken cancellationToken = default)
        {
            var req = new JObject
                {
                    { "accessToken", session.AccessToken },
                    { "clientToken", session.ClientToken },
                    { "selectedProfile", new JObject()
                        {
                            { "id", session.UUID },
                            { "name", session.Username }
                        }
                    }
                };

			using var response    = await mojangRequestAsync( "refresh", req.ToString(), cancellationToken );
			using var resStream   = await response.Content.ReadAsStreamAsync();
			using var res         = new StreamReader( resStream );
			var       rawResponse = await res.ReadToEndAsync();

			if ((int)response.StatusCode / 100 == 2)
				return this.parseSession(rawResponse, session.ClientToken);
			else
				return this.errorHandle(rawResponse);
		}

        public Task<MLoginResponse> ValidateAsync(CancellationToken cancellationToken = default)
        {
            var session = ReadSessionCache();
            return ValidateAsync(session, cancellationToken);
        }

        public async Task<MLoginResponse> ValidateAsync(MSession session, CancellationToken cancellationToken = default)
        {
            JObject req = new JObject
                {
                    { "accessToken", session.AccessToken },
                    { "clientToken", session.ClientToken }
                };

            using var response  = await mojangRequestAsync("validate", req.ToString(), cancellationToken);
			using var resStream = await response.Content.ReadAsStreamAsync();
			using var res       = new StreamReader(resStream);

			if (response.StatusCode == HttpStatusCode.NoContent) // StatusCode == 204
				return new MLoginResponse(MLoginResult.Success, session, null, null);
			else
				return new MLoginResponse(MLoginResult.NeedLogin, null, null, null);
		}

        public void DeleteTokenFile()
        {
            if (File.Exists(SessionCacheFilePath))
                File.Delete(SessionCacheFilePath);
        }

        public Task<bool> InvalidateAsync(CancellationToken cancellationToken = default)
        {
            var session = ReadSessionCache();
            return InvalidateAsync(session, cancellationToken);
        }

        public async Task<bool> InvalidateAsync(MSession session, CancellationToken cancellationToken = default)
        {
            var job = new JObject
            {
                { "accessToken", session.AccessToken },
                { "clientToken", session.ClientToken }
            };

            using var response = await mojangRequestAsync("invalidate", job.ToString(), cancellationToken);
            return response.StatusCode == HttpStatusCode.NoContent; // 204
        }

        public async Task<bool> SignoutAsync(string id, string pw, CancellationToken cancellationToken = default)
        {
            var job = new JObject
            {
                { "username", id },
                { "password", pw }
            };

            using var response = await mojangRequestAsync("signout", job.ToString(), cancellationToken);
            return response.StatusCode == HttpStatusCode.NoContent; // 204
        }
    }

    public static class HttpWebResponseExt
    {
        public static HttpWebResponse GetResponseNoException(this HttpWebRequest req)
        {
            try
            {
                return (HttpWebResponse)req.GetResponse();
            }
            catch (WebException we)
            {
                var resp = we.Response as HttpWebResponse;
                if (resp == null)
                    throw;
                return resp;
            }
        }
    }
}
