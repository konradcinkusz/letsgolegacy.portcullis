namespace Portcullis.Engine.Semantics;

/// <summary>
/// Declarations — signatures only, no behaviour — of the .NET Framework APIs the migration
/// rules detect, compiled by <see cref="ScanReferences"/> into a reference the scan's
/// compilation binds against.
///
/// The CLI scan has no project build to take references from, and no modern .NET runtime
/// ships these types: System.Web's core request types and the static configuration entry
/// points exist only in the .NET Framework (or, on modern .NET, in compatibility packages
/// the scan does not restore). Declaring them here is what lets the same symbol-based rules
/// that run inside a real build (the analyzer package, where the real assemblies are
/// referenced) bind the same names in a CLI scan: <c>HttpContext.Current</c> resolves to a
/// static property of <c>System.Web.HttpContext</c> in both, and a rule never has to guess
/// from text.
///
/// Scope, stated rather than implied: System.Web's core types (the request, response,
/// session, cache and application types, the module and handler interfaces), the
/// <c>System.Web.Caching</c>, <c>SessionState</c>, <c>Hosting</c>, <c>Security</c> and
/// <c>Configuration</c> entry points, and <c>System.Configuration</c>'s
/// <c>ConfigurationManager</c> family. System.Web.Mvc, System.Web.UI and System.Web.Http
/// are not declared: a reference to them is still caught, at its <c>using</c> directive and
/// at the bound <c>System.Web</c> prefix of a qualified name, but an unqualified type name
/// from those namespaces binds to nothing in a CLI scan. docs/rules/MIGRATION.md lists
/// this among the rules' known gaps.
///
/// This text is a string, not code: it is compiled only into the scan's own compilation,
/// never into Portcullis, and it declares nothing a scanned repository could collide with
/// except the .NET Framework types it stands in for.
/// </summary>
internal static class LegacyApiSurface
{
    public const string AssemblyName = "Portcullis.LegacyApiSurface";

    public const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        using System.IO;
        using System.Security.Principal;

        namespace System.Web
        {
            public sealed class HttpContext : IServiceProvider
            {
                public HttpContext(HttpRequest request, HttpResponse response) { }
                public static HttpContext Current { get; set; }
                public HttpRequest Request => null;
                public HttpResponse Response => null;
                public HttpServerUtility Server => null;
                public System.Web.SessionState.HttpSessionState Session => null;
                public System.Web.Caching.Cache Cache => null;
                public HttpApplicationState Application => null;
                public HttpApplication ApplicationInstance { get; set; }
                public IDictionary Items => null;
                public IPrincipal User { get; set; }
                public DateTime Timestamp => default;
                public object GetService(Type serviceType) => null;
            }

            public abstract class HttpContextBase : IServiceProvider
            {
                public virtual HttpRequestBase Request => null;
                public virtual HttpResponseBase Response => null;
                public virtual HttpServerUtilityBase Server => null;
                public virtual HttpSessionStateBase Session => null;
                public virtual System.Web.Caching.Cache Cache => null;
                public virtual IDictionary Items => null;
                public virtual IPrincipal User { get; set; }
                public virtual object GetService(Type serviceType) => null;
            }

            public class HttpContextWrapper : HttpContextBase
            {
                public HttpContextWrapper(HttpContext httpContext) { }
            }

            public sealed class HttpRequest
            {
                public NameValueCollection QueryString => null;
                public NameValueCollection Form => null;
                public NameValueCollection Headers => null;
                public NameValueCollection ServerVariables => null;
                public NameValueCollection Params => null;
                public HttpCookieCollection Cookies => null;
                public HttpFileCollection Files => null;
                public Uri Url => null;
                public Uri UrlReferrer => null;
                public string RawUrl => null;
                public string Path => null;
                public string ApplicationPath => null;
                public string PhysicalApplicationPath => null;
                public string HttpMethod => null;
                public string UserAgent => null;
                public string UserHostAddress => null;
                public string ContentType { get; set; }
                public Stream InputStream => null;
                public bool IsAuthenticated => false;
                public bool IsSecureConnection => false;
                public string this[string key] => null;
            }

            public abstract class HttpRequestBase
            {
                public virtual NameValueCollection QueryString => null;
                public virtual NameValueCollection Form => null;
                public virtual NameValueCollection Headers => null;
                public virtual HttpCookieCollection Cookies => null;
                public virtual Uri Url => null;
                public virtual string HttpMethod => null;
                public virtual string UserHostAddress => null;
                public virtual bool IsAuthenticated => false;
                public virtual string this[string key] => null;
            }

            public class HttpRequestWrapper : HttpRequestBase
            {
                public HttpRequestWrapper(HttpRequest httpRequest) { }
            }

            public sealed class HttpResponse
            {
                public int StatusCode { get; set; }
                public string StatusDescription { get; set; }
                public string ContentType { get; set; }
                public HttpCookieCollection Cookies => null;
                public NameValueCollection Headers => null;
                public TextWriter Output => null;
                public Stream OutputStream => null;
                public void Write(string s) { }
                public void AddHeader(string name, string value) { }
                public void AppendHeader(string name, string value) { }
                public void Redirect(string url) { }
                public void Redirect(string url, bool endResponse) { }
                public void Clear() { }
                public void Flush() { }
                public void End() { }
            }

            public abstract class HttpResponseBase
            {
                public virtual int StatusCode { get; set; }
                public virtual string ContentType { get; set; }
                public virtual HttpCookieCollection Cookies => null;
                public virtual void Write(string s) { }
                public virtual void Redirect(string url) { }
                public virtual void End() { }
            }

            public class HttpResponseWrapper : HttpResponseBase
            {
                public HttpResponseWrapper(HttpResponse httpResponse) { }
            }

            public sealed class HttpServerUtility
            {
                public string MapPath(string path) => null;
                public string UrlEncode(string s) => null;
                public string UrlDecode(string s) => null;
                public string HtmlEncode(string s) => null;
                public string HtmlDecode(string s) => null;
                public void Transfer(string path) { }
                public Exception GetLastError() => null;
                public void ClearError() { }
            }

            public abstract class HttpServerUtilityBase
            {
                public virtual string MapPath(string path) => null;
            }

            public abstract class HttpSessionStateBase
            {
                public virtual object this[string name] { get => null; set { } }
                public virtual string SessionID => null;
                public virtual void Abandon() { }
            }

            public sealed class HttpCookie
            {
                public HttpCookie(string name) { }
                public HttpCookie(string name, string value) { }
                public string Name { get; set; }
                public string Value { get; set; }
                public string Path { get; set; }
                public string Domain { get; set; }
                public DateTime Expires { get; set; }
                public bool HttpOnly { get; set; }
                public bool Secure { get; set; }
                public NameValueCollection Values => null;
                public string this[string key] { get => null; set { } }
            }

            public sealed class HttpCookieCollection
            {
                public HttpCookie this[string name] => null;
                public HttpCookie this[int index] => null;
                public int Count => 0;
                public void Add(HttpCookie cookie) { }
                public void Set(HttpCookie cookie) { }
                public HttpCookie Get(string name) => null;
                public void Remove(string name) { }
            }

            public sealed class HttpPostedFile
            {
                public string FileName => null;
                public string ContentType => null;
                public int ContentLength => 0;
                public Stream InputStream => null;
                public void SaveAs(string filename) { }
            }

            public abstract class HttpPostedFileBase
            {
                public virtual string FileName => null;
                public virtual int ContentLength => 0;
                public virtual Stream InputStream => null;
                public virtual void SaveAs(string filename) { }
            }

            public sealed class HttpFileCollection
            {
                public HttpPostedFile this[string name] => null;
                public HttpPostedFile this[int index] => null;
                public int Count => 0;
            }

            public class HttpApplication : IHttpHandler
            {
                public HttpContext Context => null;
                public HttpRequest Request => null;
                public HttpResponse Response => null;
                public HttpServerUtility Server => null;
                public System.Web.SessionState.HttpSessionState Session => null;
                public HttpApplicationState Application => null;
                public bool IsReusable => false;
                public void ProcessRequest(HttpContext context) { }
                public void CompleteRequest() { }
            }

            public sealed class HttpApplicationState
            {
                public object this[string name] { get => null; set { } }
                public void Lock() { }
                public void UnLock() { }
            }

            public sealed class HttpRuntime
            {
                public static string AppDomainAppPath => null;
                public static string AppDomainAppVirtualPath => null;
                public static string AppDomainAppId => null;
                public static System.Web.Caching.Cache Cache => null;
            }

            public class HttpException : Exception
            {
                public HttpException() { }
                public HttpException(string message) { }
                public HttpException(int httpCode, string message) { }
                public int GetHttpCode() => 0;
            }

            public interface IHttpModule
            {
                void Init(HttpApplication context);
                void Dispose();
            }

            public interface IHttpHandler
            {
                bool IsReusable { get; }
                void ProcessRequest(HttpContext context);
            }

            public static class VirtualPathUtility
            {
                public static string ToAbsolute(string virtualPath) => null;
                public static string ToAppRelative(string virtualPath) => null;
                public static string Combine(string basePath, string relativePath) => null;
            }
        }

        namespace System.Web.SessionState
        {
            public sealed class HttpSessionState
            {
                public object this[string name] { get => null; set { } }
                public string SessionID => null;
                public int Timeout { get; set; }
                public int Count => 0;
                public void Abandon() { }
                public void Clear() { }
                public void Remove(string name) { }
            }
        }

        namespace System.Web.Caching
        {
            public sealed class Cache
            {
                public object this[string key] { get => null; set { } }
                public object Get(string key) => null;
                public void Insert(string key, object value) { }
                public object Remove(string key) => null;
            }
        }

        namespace System.Web.Hosting
        {
            public static class HostingEnvironment
            {
                public static string ApplicationPhysicalPath => null;
                public static string ApplicationVirtualPath => null;
                public static bool IsHosted => false;
                public static string MapPath(string virtualPath) => null;
                public static void QueueBackgroundWorkItem(Action<System.Threading.CancellationToken> workItem) { }
            }
        }

        namespace System.Web.Security
        {
            public static class FormsAuthentication
            {
                public static void SetAuthCookie(string userName, bool createPersistentCookie) { }
                public static void SignOut() { }
                public static void RedirectFromLoginPage(string userName, bool createPersistentCookie) { }
                public static string LoginUrl => null;
            }

            public static class Roles
            {
                public static bool IsUserInRole(string roleName) => false;
                public static string[] GetRolesForUser() => null;
            }
        }

        namespace System.Web.Configuration
        {
            public static class WebConfigurationManager
            {
                public static NameValueCollection AppSettings => null;
                public static System.Configuration.ConnectionStringSettingsCollection ConnectionStrings => null;
                public static object GetSection(string sectionName) => null;
                public static object GetWebApplicationSection(string sectionName) => null;
            }
        }

        namespace System.Configuration
        {
            public static class ConfigurationManager
            {
                public static NameValueCollection AppSettings => null;
                public static ConnectionStringSettingsCollection ConnectionStrings => null;
                public static object GetSection(string sectionName) => null;
                public static void RefreshSection(string sectionName) { }
                public static Configuration OpenExeConfiguration(ConfigurationUserLevel userLevel) => null;
                public static Configuration OpenExeConfiguration(string exePath) => null;
                public static Configuration OpenMachineConfiguration() => null;
            }

            public enum ConfigurationUserLevel
            {
                None = 0,
                PerUserRoaming = 10,
                PerUserRoamingAndLocal = 20,
            }

            public sealed class Configuration
            {
                public string FilePath => null;
                public object GetSection(string sectionName) => null;
                public void Save() { }
            }

            [Obsolete("Superseded by System.Configuration.ConfigurationManager in the .NET Framework itself.")]
            public sealed class ConfigurationSettings
            {
                public static NameValueCollection AppSettings => null;
                public static object GetConfig(string sectionName) => null;
            }

            public sealed class ConnectionStringSettings
            {
                public string Name { get; set; }
                public string ConnectionString { get; set; }
                public string ProviderName { get; set; }
            }

            public sealed class ConnectionStringSettingsCollection : IEnumerable
            {
                public ConnectionStringSettings this[string name] => null;
                public ConnectionStringSettings this[int index] => null;
                public int Count => 0;
                public IEnumerator GetEnumerator() => null;
            }
        }
        """;
}
