﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Videre.Core.Extensions;
using CodeEndeavors.Extensions;
using System.Configuration;
using Videre.Core.Models;
using Videre.Core.ActionResults;
using System.Web;

namespace Videre.Core.Services
{
    public class Package
    {
        public static string PackageDir
        {
            get
            {
                return Portal.ResolvePath(Portal.GetAppSetting("PackageDir", "~/_packages/"));
            }
        }

        public static string PublishDir
        {
            get
            {
                return Portal.ResolvePath(Portal.GetAppSetting("PublishDir", "~/_publish/"));
            }
        }

        public static string RemotePackageUrl
        {
            get 
            { 
                var url = Portal.CurrentPortal.GetAttribute("Core", "RemotePackageUrl", "");
                if (!url.EndsWith("core/package/", StringComparison.InvariantCultureIgnoreCase))
                    url = url.PathCombine("core/package/", "/");
                return url;
            }
        }

        public static List<Models.Package> GetNewestAvailablePackages()
        {
            var packages = GetAvailablePackages();
            return packages.GroupBy(p => p.Name, p => p).Select(p => p.OrderByDescending(p2 => p2.Version).First()).ToList();
        }

        public static List<Models.Package> GetAvailablePackages()
        {
            return GetPackagesFromDir(PackageDir);
        }

        public static Models.Package GetPublishedPackage(string name, string version)
        {
            return GetPublishedPackages().Where(p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && p.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
        }
        public static List<Models.Package> GetPublishedPackages()
        {
            return GetPackagesFromDir(PublishDir);
        }

        public static List<Models.Package> GetPackagesFromDir(string dir)
        {
            var packages = new List<Models.Package>();
            var packageDir = Portal.ResolvePath(dir);
            if (!Directory.Exists(packageDir))
                Directory.CreateDirectory(packageDir);

            foreach (var file in Directory.GetFiles(packageDir, "*.zip"))
            {
                var manifest = GetPackageManifest(file);
                if (manifest != null)
                {
                    manifest.FileName = file;
                    packages.Add(manifest);
                }
            }
            return packages;
        }

        public static string AddPackage(string fileName)
        {
            var manifest = GetPackageManifest(fileName);
            if (manifest == null)
                throw new Exception(Localization.GetLocalization(LocalizationType.Exception, "PackageRequiresManifest.Error", "Package requires a manifest.", "Core"));

            var destFileName = PackageDir.PathCombine(new FileInfo(fileName).Name, @"\");
            if (new FileInfo(fileName).FullName != new FileInfo(destFileName).FullName)
            {
                if (System.IO.File.Exists(destFileName))
                    System.IO.File.Delete(destFileName);
                System.IO.File.Move(fileName, destFileName);
            }
            return destFileName;
        }

        public static bool InstallAvailablePackage(string name, string version = null, string portalId = null)
        {
            portalId = string.IsNullOrEmpty(portalId) ? Portal.CurrentPortalId : portalId;

            var package = GetAvailablePackages().Where(p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && (string.IsNullOrEmpty(version) || p.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase))).OrderByDescending(p => p.Version).FirstOrDefault();
            if (package != null)
            {
                InstallFile(Path.Combine(Portal.ResolvePath(PackageDir), package.FileName), portalId, false);
                //System.IO.File.Copy(Path.Combine(Portal.ResolvePath(PackageDir), package.FileName), Path.Combine(Portal.ResolvePath(UpdateDir), package.FileName), true);
                return true;
            }
            return false;
        }

        public static bool RemoveAvailablePackage(string name, string version)
        {
            var package = GetAvailablePackages().Where(p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && (string.IsNullOrEmpty(version) || p.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase))).OrderByDescending(p => p.Version).FirstOrDefault();
            if (package != null)
            {
                System.IO.File.Delete(package.FileName);
                return true;
            }
            return false;
        }

        public static bool UninstallPackage(string name, string version)
        {
            var package = GetInstalledPackages().Where(p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && (string.IsNullOrEmpty(version) || p.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase))).OrderByDescending(p => p.Version).FirstOrDefault();
            if (package != null)
            {
                //todo: get manifest/files from available package zip and do the right thing...
                DeletePackage(package.Id);
                RemoveAvailablePackage(name, version);
                return true;
            }
            return false;
        }

        public static bool TogglePublishPackage(string name, string version, bool publish)
        {
            if (publish)
            {
                var package = GetAvailablePackages().Where(p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && (string.IsNullOrEmpty(version) || p.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase))).OrderByDescending(p => p.Version).FirstOrDefault();
                if (package != null)
                {
                    System.IO.File.Copy(package.FileName, PublishDir.PathCombine(new FileInfo(package.FileName).Name, @"\"), true);
                    return true;
                }
            }
            else
            {
                var package = GetPublishedPackages().Where(p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && (string.IsNullOrEmpty(version) || p.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase))).OrderByDescending(p => p.Version).FirstOrDefault();
                if (package != null)
                {
                    System.IO.File.Delete(package.FileName);
                    return true;
                }
            }
            return false;
        }

        public static Models.Package GetPackageManifest(string zipFileName)
        {
            var entryName = zipFileName.GetZipFileList(e => e.EndsWith("package.manifest", StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
            if (!string.IsNullOrEmpty(entryName))
                return zipFileName.GetZipEntryContents(entryName).ToObject<Models.Package>();
            else
                return null;
        }

        public static List<Models.PortalExport> GetPackageContent(string zipFileName)
        {
            var ret = new List<Models.PortalExport>();
            var entries = zipFileName.GetZipFileList(e => e.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase)).ToList();
            foreach (var entry in entries)
                ret.Add(zipFileName.GetZipEntryContents(entry).ToObject<Models.PortalExport>());
            return ret;
        }

        public static int ApplyPackageContent(string zipFileName, string portalId)
        {
            var count = 0;
            
            var content = GetPackageContent(zipFileName);
            foreach (var portalExport in content)
            {
                Services.ImportExport.Import(portalExport, portalId);
                count++;
            }
            //var entries = zipFileName.GetZipFileList(e => e.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase)).ToList();
            //foreach (var entry in entries)
            //{
            //    zipFileName.ExtractEntry(entry, Update.UpdateDir);
            //    count++;
            //}
            return count;
        }

        public static bool InstallFile(string fileName, string portalId = null, bool removeFile = true)
        {
            var rootDir = Portal.ResolvePath("~/");
            var file = new FileInfo(fileName);
            portalId = string.IsNullOrEmpty(portalId) ? Portal.CurrentPortalId : portalId;

            if (file != null)
            {
                switch (file.Extension.ToLower())
                {
                    case ".zip":
                        {
                            //todo: dependency checks
                            var packageManifest = GetPackageManifest(fileName);
                            
                            var packageFileName = packageManifest != null ? AddPackage(fileName) : fileName;   //if zip has manifest put in package folder and install from there

                            Logging.Logger.InfoFormat("Applying update for file: {0}", packageFileName);
                            packageFileName.ExtractZip(rootDir, @"-package\.manifest;-\.json");

                            //todo:  deferred package content apply or right now?  
                            //ideally manifest will have the registration assembly listed and we can register right away then instantly apply
                            ApplyPackageContent(packageFileName, portalId);

                            if (removeFile)
                                System.IO.File.Delete(file.FullName);

                            if (packageManifest != null)
                            {
                                var existingPackage = GetInstalledPackage(packageManifest.Name);
                                if (existingPackage != null)
                                    packageManifest.Id = existingPackage.Id; //todo: odd to do this..
                                packageManifest.FileName = packageFileName;
                                packageManifest.InstallDate = DateTime.UtcNow;
                                Save(packageManifest);
                            }

                            return true;
                        }
                    case ".json":
                        {
                            Logging.Logger.InfoFormat("Applying import for file: {0}", file.FullName);
                            var portalExport = file.FullName.GetFileJSONObject<Models.PortalExport>(false);
                            Services.ImportExport.Import(portalExport, portalId);
                            if (removeFile)
                                System.IO.File.Delete(file.FullName);
                            return true;
                        }
                    default:
                        {
                            Logging.Logger.Error("Unknown File Extension: " + file.Extension);
                            break;
                            //throw new Exception("Unknown File Extension: " + file.Extension);
                        }
                }
            }
            return false;
        }

        public static List<Models.Package> GetRemotePackages()
        {
            var packages = new List<Models.Package>();
            if (!string.IsNullOrEmpty(RemotePackageUrl))
            {
                var client = new System.Net.WebClient();
                var json = client.DownloadString(RemotePackageUrl.PathCombine("getpublishedpackages", "/"));
                packages = json.ToObject<List<Models.Package>>();
            }
            return packages;
        }

        public static Models.Package GetRemotePackage(string name, string version)
        {
            return GetRemotePackages().Where(p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && p.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
        }

        public static bool DownloadRemotePackage(string name, string version)
        {
            if (!string.IsNullOrEmpty(RemotePackageUrl))
            {
                var remotePackage = GetRemotePackage(name, version); 
                if (remotePackage != null)
                {
                    var client = new System.Net.WebClient();
                    var fileName = remotePackage.FileName.Substring(remotePackage.FileName.LastIndexOf(@"\")+1);
                    var downloadFileName = PackageDir.PathCombine(fileName, @"\");
                    
                    client.DownloadFile(RemotePackageUrl.PathCombine("getpublishedpackage", "/") + "?name=" + HttpUtility.UrlEncode(name) + "&version=" + HttpUtility.UrlEncode(version), downloadFileName);   
                    
                    if (new FileInfo(downloadFileName).Length == 0) //if no data found... delete it... 
                    {
                        System.IO.File.Delete(downloadFileName);
                        return false;
                    }
                    return true;
                }
            }
            return false;
        }

        public static List<Models.Package> GetInstalledPackages()
        {
            return Repository.Current.GetResources<Models.Package>("Package").Select(m => m.Data).OrderBy(i => i.Name).ToList();
        }
        public static Models.Package GetInstalledPackage(string name)
        {
            return GetInstalledPackages().Where(m => m.Name == name).FirstOrDefault();
        }
        public static Models.Package GetPackageById(string id)
        {
            return GetInstalledPackages().Where(m => m.Id == id).FirstOrDefault();
        }
        //public static string Import(Models.Package manifest, string userId = null)
        //{
        //    userId = string.IsNullOrEmpty(userId) ? Account.AuditId : userId;
        //    var existing = GetPackage(manifest.FullName);
        //    manifest.Id = existing != null ? existing.Id : null;
        //    return Save(manifest, userId);
        //}
        public static string Save(Models.Package package, string userId = null)
        {
            userId = string.IsNullOrEmpty(userId) ? Account.AuditId : userId;
            if (!IsDuplicate(package))
            {
                var res = Repository.Current.StoreResource("Package", null, package, userId);
                return res.Id;
            }
            else
                throw new Exception(string.Format(Localization.GetLocalization(LocalizationType.Exception, "DuplicateResource.Error", "{0} already exists.   Duplicates Not Allowed.", "Core"), "Package"));
        }
        public static bool IsDuplicate(Models.Package package)
        {
            var m = GetInstalledPackage(package.Name);
            if (m != null)
                return m.Id != package.Id;
            return false;
        }
        public static bool Exists(Models.Package package)
        {
            var m = GetInstalledPackage(package.Name);
            return (m != null);
        }
        public static bool DeletePackage(string id, string userId = null)
        {
            userId = string.IsNullOrEmpty(userId) ? Account.AuditId : userId;
            var res = Repository.Current.GetResourceById<Models.Package>(id);
            if (res != null)
            {
                //throw new Exception("NOT IMPLEMENTED YET!");
                Repository.Current.Delete(res);
            }
            return res != null;
        }


    }
}
