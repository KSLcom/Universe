﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using NuGet;

namespace NuGetClone
{
    public class MyService : ServiceBase
    {
        private static readonly Uri DeveloperFeed = new Uri("https://www.myget.org/F/aspnetvnext/api/v2");
        private static readonly ICredentials _credentials = new NetworkCredential("aspnetreadonly", "4d8a2d9c-7b80-4162-9978-47e918c9658c");

        private Timer _timer;
        private string _targetDirectory;

        protected override void OnStart(string[] args)
        {
            base.OnStart(args);
            Init();
            _timer = new Timer(Run, null, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2));
        }

        public void Init()
        {
            _targetDirectory = Environment.GetEnvironmentVariable("PROJECTK_PACKAGE_CACHE");
            if (string.IsNullOrEmpty(_targetDirectory))
            {
                _targetDirectory = @"c:\projectk-cache";
            }
        }

        public void Run(object state)
        {
            try
            {
                RunFromGallery();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        public void RunFromGallery()
        {
            var client = new HttpClient(DeveloperFeed);
            client.SendingRequest += (sender, e) =>
            {
                e.Request.Credentials = _credentials;
                e.Request.PreAuthenticate = true;
            };
            var remoteRepo = new DataServicePackageRepository(client);
            var targetRepo = new LocalPackageRepository(_targetDirectory);
            var packages = remoteRepo.GetPackages()
                                     .Where(p => p.IsLatestVersion);
            Parallel.ForEach(packages, package =>
            {
                if (!targetRepo.Exists(package))
                {
                    PurgeOldVersions(targetRepo, package);

                    var packagePath = GetPackagePath(package);

                    using (var input = package.GetStream())
                    using (var output = File.Create(packagePath))
                    {
                        input.CopyTo(output);
                    }
                }
            });
        }

        private string GetPackagePath(IPackage package)
        {
            return Path.Combine(_targetDirectory, package.GetFullName() + ".nupkg");
        }

        private void PurgeOldVersions(LocalPackageRepository targetRepo, IPackage package)
        {
            foreach (var oldPackage in targetRepo.FindPackagesById(package.Id).Where(p => p.Version < package.Version))
            {
                try
                {
                    var path = GetPackagePath(oldPackage);
                    File.Delete(path);
                }
                catch
                {
                    // Ignore
                }
            }
        }


        protected override void OnStop()
        {
            base.OnStop();
        }
    }
}
