﻿using StructureMap;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Videre.Core.Providers;
using CoreModels = Videre.Core.Models;
using CoreServices = Videre.Core.Services;
using CodeEndeavors.Extensions;
using System.Web.Security;
using System.Web;

namespace Videre.Core.Services
{
    public class AuthenticationResult
    {
        public AuthenticationResult()
        {
            Errors = new List<string>();
        }
        public bool Success { get; set; }
        public List<string> Errors { get; set; }
        public string Provider { get; set; }
        public string ProviderUserId { get; set; }
        public string UserName { get; set; }
        public IDictionary<string, string> ExtraData { get; set; }
    }

    public class Authentication
    {
        private const string _authenticationClaimType = "AuthenticationToken";
        private static List<IAuthenticationProvider> _authenticationProviders = new List<IAuthenticationProvider>();

        public static void IssueAuthenticationTicket(string identityName, List<string> roles, int days, bool persistant)
        {
            var ticket = new FormsAuthenticationTicket(1, identityName, DateTime.Now, DateTime.Now.AddDays(days), persistant, String.Join(",", roles));
            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket));
            cookie.Expires = ticket.Expiration;
            HttpContext.Current.Response.Cookies.Add(cookie);
        }

        public static void RevokeAuthenticationTicket()
        {
            FormsAuthentication.SignOut();
        }

        public static void RegisterAuthenticationProviders()
        {
            ObjectFactory.Configure(x =>
                x.Scan(scan =>
                {
                    scan.AssembliesFromApplicationBaseDirectory();
                    scan.AddAllTypesOf<IAuthenticationProvider>();
                }));
            _authenticationProviders = ObjectFactory.GetAllInstances<IAuthenticationProvider>().ToList();
            foreach (var provider in _authenticationProviders)
                provider.Register();
        }

        public static List<IAuthenticationProvider> GetAuthenticationProviders()
        {
            return _authenticationProviders;
        }

        public static IAuthenticationProvider GetAuthenticationProvider(string provider)
        {
            return GetAuthenticationProviders().Where(p => p.Name.Equals(provider, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
        }

        public static List<string> GetUserAuthenticationProviders(CoreModels.User user)
        {
            return user.Claims.Where(c => c.Type == _authenticationClaimType).Select(c => c.Issuer).ToList();
        }

        public static CoreModels.User GetUserByAuthenticationToken(string provider, string token, string portalId = null)
        {
            return CoreServices.Account.Get(u => u.Claims.Exists(c => c.Type == _authenticationClaimType && c.Issuer == provider), portalId);
        }

        public static CoreModels.User AssociateAuthenticationToken(string userId, string provider, string token)
        {
            var user = CoreServices.Account.GetUserById(userId);
            var existing = GetUserByAuthenticationToken(provider, token, user.PortalId);
            if (existing == null || existing.Id == user.Id)
            {
                var claim = user.GetClaim(_authenticationClaimType, provider);
                if (claim == null)
                {
                    claim = new CoreModels.UserClaim() { Issuer = provider, Type = _authenticationClaimType };
                    user.Claims.Add(claim);
                }
                claim.Value = token;

                CoreServices.Account.SaveUser(user);
                return user;
            }
            else
                throw new Exception("Authentication Token already associated with another user");
        }

        public static CoreModels.User DisassociateAuthenticationToken(string userId, string provider)
        {
            var user = CoreServices.Account.GetUserById(userId);
            if (user.Claims.Exists(c => c.Type == _authenticationClaimType))
            {
                var authCount = user.Claims.Where(c => c.Type == _authenticationClaimType).Count(); 
                if (!string.IsNullOrEmpty(user.PasswordHash) || authCount > 1)
                {
                    var claim = user.GetClaim(_authenticationClaimType, provider);
                    if (claim != null)
                        user.Claims.Remove(claim);
                    CoreServices.Account.SaveUser(user);
                    return user;
                }
                else
                    throw new Exception("User cannot remove last form of credentials");
            }
            else
                throw new Exception("User not associated with Authentication Token");
        }

        //needs to be exactly the same between ExternalLogin and ExternLoginCallback methods    - hacky that the service knows URL
        public static string GetExternalLoginCallbackUrl(string provider, string returnUrl, bool associate)
        {
            return CoreServices.Portal.RequestRootUrl.PathCombine(CoreServices.Portal.ResolveUrl(string.Format("~/core/Account/ExternalLogInCallback?provider={0}&returnUrl={1}&associate={2}", System.Web.HttpUtility.UrlEncode(provider), System.Web.HttpUtility.UrlEncode(returnUrl), associate)), "/").ToLower();
        }

        public static bool ProcessExternalAuthentication(string provider, string returnUrl, bool associate)
        {
            var authProvider = CoreServices.Authentication.GetAuthenticationProvider(provider);
            if (authProvider != null)
            {
                var result = authProvider.VerifyAuthentication(GetExternalLoginCallbackUrl(provider, returnUrl, associate));
                if (result.Success)
                {
                    //if authenticated, then we are in user profile, and associating    - todo: better way to detects?   probably simply use another controller and method
                    //if (CoreServices.Account.IsAuthenticated)
                    if (associate)
                    {
                        if (CoreServices.Account.IsAuthenticated)
                            CoreServices.Authentication.AssociateAuthenticationToken(CoreServices.Account.CurrentUser.Id, result.Provider, result.ProviderUserId);
                        else
                            throw new Exception("Cannot associate Authentication without logged in user");
                    }
                    else
                    {
                        var user = GetUserByAuthenticationToken(result.Provider, result.ProviderUserId);
                        if (user != null)
                            IssueAuthenticationTicket(user.Id.ToString(), user.RoleIds, 30, true);
                        else
                            throw new Exception("No user is associated with this provider");
                    }
                    return true;
                }
                else
                    throw new Exception("External Authentication Failed: " + result.Errors.ToJson());//return RedirectToAction("ExternalLoginFailure");
            }
            else
                throw new Exception("Authentication Provider not found: " + provider);

        }


    }
}