using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Threading;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text.RegularExpressions;

using CommandLine;


namespace update_package_reference
{
    class reference_update
    {
        bool parseErr = false;

        // Obfuscating this will break the commandline parser
        [ObfuscationAttribute(Exclude = true, ApplyToMembers = true)]
        internal class FWUpdateCLIOptions
        {
            //private readonly IEnumerable<string> _excludes;

            public FWUpdateCLIOptions()//IEnumerable<string> excludes)
            {
                //_excludes = excludes;
                //_excludes = new List<string>();
            }

            [Option('v', "verbose", Required = false, Default = false, HelpText = "Set output to verbose messages (if omitted verbose mode is off).")]
            public bool Verbose { get; set; }

            [Option('p', "package", Required = true, HelpText = "The name of the package that will get an updated reference in the target project files.  tag is used to pare down a repo listing to a collection that includes this value.")]
            public string PackageID { get; set; }

            [Option('t', "tag", Required = false, HelpText = "A string that will be used to help find the package to be upgraded.  A tag can be a name or a value contained within a package tags collection. tag is used to narrow down the repo listing to as few entries as possible in addition to the desired package name.  If a tag is not provided, the package name will be used as the tag.")]
            public string Tag { get; set; }

            [Option('s', "source", Required = false, Default = "", HelpText = "A URI of a source to browse for the desired package.")]
            public string Source { get; set; }

            [Option('c', "csproj", Required = true, HelpText = "The project file(s) to receive the updated Package Reference.")]
            public string ProjectFilespec { get; set; }

            [Option('d', "dryrun", Required = false, Default = false, HelpText = "Prevents the utility from making any proejct file changes.  Everything up to that point procedes normally.")]
            public bool DryRun { get; set; }

            [Option('x', "exact", Required = false, HelpText = "The version of the package that is to be referenced.  This allows for explicit version ranges to be specified.  If an exact version is provided no online package seatch is performed - tag and source options are ignored if provided.")]
            public bool exact { get; set; }

            [Option('e', "explicit", Required = false, HelpText = "The version of the package that is to be referenced.  This allows for explicit version ranges to be specified.  If an exact version is provided no online package seatch is performed - tag and source options are ignored if provided.")]
            public string Version { get; set; }



            //[Option('e', "exclude", Separator = ',',  Required = false, HelpText = "Names a folder to be excluded in the csproj search.  Absolute Paths only.  May be specified multiple times.")]
            ////public string[] Exclude { get; set; }
            //public IEnumerable<string> Exclude { get; set; }

        }

        // Called when there is a problem parsing CLI parameters
        void HandleParseError(IEnumerable<Error> errs)
        {
            if (errs.Any(x => x is HelpRequestedError)) //|| x is VersionRequestedError))
            {
                Console.WriteLine("Example:\n");
                Console.WriteLine(@"    update_package_reference -t searchtag -p My.Nuget.Package -c c:\projects\Control\SuperConfig\*.csproj -s https://extron.jfrog.io/extron/api/nuget/nuget-test -v -dryrun");
                Console.WriteLine("");
            }
            parseErr = true;
        }

        // The app needs to query the repo to find the latest version of the desired package.
        // Search results consist of a package name (ID) and a version.  This class contains that information.
        class MatchingPackage
        {
            string verStr;
            Version ver;

            internal MatchingPackage() { verStr = ""; ver = new Version(); }

            internal string PackageName { set; get; }
            internal string PackageVersionStr
            {
                set
                {
                    verStr = value;
                    ver = new Version(verStr);
                }
                get
                {
                    return verStr;
                }
            }

            internal Version PackageVersion
            {
                get
                {
                    return ver;
                }
            }

        }

        string[] _GetFiles(string folder, string filespec, string[] excludes)
        {
            string[] shallowFiles = Directory.GetFiles(folder, filespec, SearchOption.TopDirectoryOnly);
            string[] shallowFolders = Directory.GetDirectories(folder);
            string[] foldersToScan = shallowFolders.Where(x => !excludes.Contains(x.ToLower())).ToArray();
            foreach ( string scanFolder in foldersToScan )
            {
                string[] deepFiles = _GetFiles(scanFolder, filespec, excludes);

                string[] accum = new string[deepFiles.Length + shallowFiles.Length];
                shallowFiles.CopyTo(accum, 0);
                deepFiles.CopyTo(accum, shallowFiles.Length);
                shallowFiles = accum;
            }
            return shallowFiles;
        }

        string[] GetFiles(string folder, string filespec, string[] excludes)
        {
            string resolved = System.IO.Path.GetFullPath(folder);
            string[] lcExcludes = excludes.Select(x => x.ToLower() ).ToArray();
            return _GetFiles(resolved, filespec, lcExcludes);
        }

        string[] GetFiles(string filespec, string[] excludes)
        {
            string folder = System.IO.Path.GetDirectoryName(filespec);

            string spec = filespec.Substring(folder.Length);

            while ( (spec.Length > 0) && ((spec[0] == '\\') || (spec[0] == '/')))
            {
                spec = spec.Substring(1);
            }

            if ( spec.Length > 0 )
            {
                return GetFiles(folder, spec, excludes);
            }
            else
            {
                return new string[0];
            }
        }


        internal Tuple<int,string[]> GetProjectFiles(string projectFilespec, bool verbose)
        {
            // Step 3 - find a list of all matching project files
            string rootFolder = Path.GetDirectoryName(projectFilespec);
            string filespec = Path.GetFileName(projectFilespec);

            int retval = 0;

            if (verbose)
            {
                Console.WriteLine("Searching for {0} in {1}", filespec, rootFolder);
            }

            string[] projectFiles = null;

            try
            {
                projectFiles = Directory.GetFiles(rootFolder, filespec, SearchOption.AllDirectories);
            }
            catch
            {
                // Second attempt
                Console.WriteLine("Adjusted search for project files from CWD: {0}", System.IO.Directory.GetCurrentDirectory());

                if (rootFolder[rootFolder.Length - 1] != '\\')
                    rootFolder += '\\';
                filespec = projectFilespec.Substring(rootFolder.Length);

                try
                {
                    projectFiles = Directory.GetFiles(rootFolder, filespec, SearchOption.AllDirectories);
                }
                catch
                {
                    retval = 5; // Error scanning for project files.  Bad filespec?
                }
            }

            if (projectFiles.Count() == 0)
            {
                retval = 3; // no project files to modify
            }

            if (verbose)
            {
                foreach (string projFile in projectFiles)
                {
                    Console.WriteLine("    {0}", projFile);
                }
            }

            return new Tuple<int, string[]>(retval, projectFiles);
        }

        internal int UpdateProjectFiles(string[] projectFiles, string packageVersionStr, string packageName, bool verbose, bool dryrun)
        {
            int retVal = 0;
            // This regex will find package references in .NET Standard projects
            // <PackageReference\s+Include="Newtonsoft.Json"\s+Version=\"(?<verRef>[\[(]?[\d.,*]+[\])]?)\"\s+\/>
            //
            // This regex will find package references in .NET Framework projects
            // <PackageReference Include=\"Newtonsoft.Json\">\s+(<Version>)(?<verRef>[\[(]?[\d.,*]+[\])]?)(<\/Version>)\s+<\/PackageReference>

            string frameworkPattern = String.Format("<PackageReference Include=\"{0}\">\\s+(<Version>)(?<verRef>[\\[(]?[\\d.,*]+[\\])]?)(<\\/Version>)\\s+<\\/PackageReference>", packageName.Replace(".", "\\."));
            Regex frameworkRegex = new Regex(frameworkPattern);

            string standardPattern = String.Format("<PackageReference\\s+Include=\"{0}\"\\s+Version=\"(?<verRef>[\\[(]?[\\d.,*]+[\\])]?)\"\\s+\\/>", packageName.Replace(".", "\\."));
            Regex standardRegex = new Regex(standardPattern);

            List<string> noMatchingReference = new List<string>();

            foreach (string projFile in projectFiles)
            {
                try
                {
                    string projType = "fx";
                    string stuff = File.ReadAllText(projFile);
                    Match pkgRef = frameworkRegex.Match(stuff);
                    if (!pkgRef.Success)
                    {
                        pkgRef = standardRegex.Match(stuff);
                        projType = "standard";
                    }
                    if (pkgRef.Success)
                    {
                        string newProjectFile = stuff.Substring(0, pkgRef.Groups["verRef"].Index);
                        newProjectFile += packageVersionStr; // Using the string instead of rendering the version object.
                        newProjectFile += stuff.Substring(pkgRef.Groups["verRef"].Index + pkgRef.Groups["verRef"].Value.Length);

                        if (dryrun)
                        {
                            Console.WriteLine("Dry Run: Would have updated {0} ({1}) PackageReference of {2} from {3} to {4}.  File not changed.", projFile, projType, packageName, pkgRef.Groups["verRef"].Value, packageVersionStr);
                        }
                        else
                        {
                            Console.WriteLine("Updating {0} ({1}) PackageReference of {2} from {3} to {4}.", projFile, projType, packageName, pkgRef.Groups["verRef"].Value, packageVersionStr);
                            try
                            {
                                File.WriteAllText(projFile, newProjectFile);
                            }
                            catch
                            {
                                Console.WriteLine("Failed to update {0} ({1}).  File locked or read-only?", projFile, projType);
                                retVal = 4;
                            }
                        }
                        continue;
                    }
                    else
                    {
                        noMatchingReference.Add(string.Format("No matching reference found in {0} ({1}).  File not changed.", projFile, projType));
                        //Console.WriteLine("No matching reference found in {0} ({1}).  File not changed.", projFile, projType);
                    }
                }
                catch
                {
                    Console.WriteLine("Error while processing {0}", projFile);
                    retVal = 4;
                }
            }

            foreach ( string entry in noMatchingReference )
            {
                Console.WriteLine(entry);
            }
            return retVal;
        }



        internal int Run(string[] args)
        {
            int retVal = 0;

            bool verbose = false;
            bool dryrun = false;
            bool exact = false;
            string searchTag = "";
            string packageName = "";
            string packageNameLC = "";
            string projectFilespec = "";
            string sourceRepo = "";
            string versionRange = "";
            bool explicitVersionReference = false;
            //IEnumerable<string> noScan = new List<string>();

            parseErr = false;


            Parser.Default.ParseArguments<FWUpdateCLIOptions>(args)
                   .WithParsed<FWUpdateCLIOptions>(o =>
                   {
                       verbose = o.Verbose;
                       dryrun = o.DryRun;
                       searchTag = o.Tag;
                       packageName = o.PackageID;
                       projectFilespec = o.ProjectFilespec;
                       sourceRepo = o.Source;
                       versionRange = o.Version;
                       exact = o.exact;

                       if (string.IsNullOrEmpty(versionRange) == false)
                       {
                           explicitVersionReference = true;
                       }
//                       noScan = o.Exclude;
                       if ( String.IsNullOrEmpty(searchTag) )
                       {
                           searchTag = packageName;
                       }


                   }).WithNotParsed(HandleParseError);

#if DEBUG
            Console.WriteLine("verbose:                  {0}",verbose);
            Console.WriteLine("dryrun:                   {0}",dryrun);
            Console.WriteLine("exact:                    {0}", exact);
            Console.WriteLine("searchTag:                {0}",searchTag);
            Console.WriteLine("packageName:              {0}",packageName);
            Console.WriteLine("projectFilespec:          {0}",projectFilespec);
            Console.WriteLine("sourceRepo:               {0}",sourceRepo);
            Console.WriteLine("versionRange:             {0}",versionRange);
            Console.WriteLine("explicitVersionReference: {0}",explicitVersionReference);
#endif

            if (parseErr == true)
            {
                retVal = 1; // param error
            }
            else
            {
                if (verbose && !explicitVersionReference)
                {
                    Console.Write("Searching for package {0} with tag {1}", packageName, searchTag);
                    if ( String.IsNullOrEmpty(sourceRepo) == false)
                    {
                        Console.Write(", looking in {0}", sourceRepo);
                    }
                    Console.Write("\n");
                }

                List<MatchingPackage> foundPackages = new List<MatchingPackage>();

                if (explicitVersionReference == false)
                {
                    // search the repo for the desired package using the provided search tag (and optional source URI).
                    // This can take a notable amound of time - thus the stopwatch for verbose mode time reports.
                    Stopwatch stopWatch = new Stopwatch();
                    stopWatch.Start();
                    string packageList = GetRawPackageList(searchTag, sourceRepo);
                    stopWatch.Stop();

                    if (verbose)
                    {
                        TimeSpan ts = stopWatch.Elapsed;
                        Console.WriteLine("Package list acquired in {0:00} minutes {1:00}.{2:00} seconds", ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                        Console.WriteLine(packageList);
                    }


                    string packageRegex = String.Format("(?<pkgStr>{0})\\s+(?<verStr>\\d+(\\.\\d+)+)", packageName.Replace(".", "\\."));
                    Regex reggie = new Regex(packageRegex, RegexOptions.IgnoreCase);
                    MatchCollection found = reggie.Matches(packageList);
                    foreach (Match entry in found)
                    {
                        try
                        {
                            foundPackages.Add(new MatchingPackage() { PackageName = entry.Groups["pkgStr"].Value, PackageVersionStr = entry.Groups["verStr"].Value });
                        }
                        catch
                        {
                            if (verbose)
                            {
                                Console.WriteLine("Could not parse {0} - discarding", packageName);
                            }
                        }
                    }
                }

                if ((foundPackages.Count == 0) && !explicitVersionReference  )
                {
                    Console.WriteLine("No packages found matching {0}", packageName);
                    retVal = 2; // no matches found
                }
                else
                {
                    if (explicitVersionReference)
                    {
                        Tuple<int,string[]> exresults = GetProjectFiles(projectFilespec, verbose);

                        retVal = exresults.Item1;
                        if (exresults.Item1 != 0)
                        {
                            goto get_outta_dodge;
                        }

                        retVal = UpdateProjectFiles(exresults.Item2, versionRange, packageName, verbose, dryrun);

                        goto get_outta_dodge;
                    }

                    if (verbose)
                    {
                        Console.WriteLine("Found {0} matching packages", packageName);
                    }

                    // find which match is the highest version (there should be only one match as this is
                    // default NuGet behavior, but we may change the code to list all versions at a later date.
                    MatchingPackage highest = foundPackages[0];
                    foreach (MatchingPackage match in foundPackages)
                    {
                        if ( match.PackageVersion > highest.PackageVersion )
                        {
                            highest = match;
                        }
                        if (verbose)
                        {
                            Console.WriteLine("    {0} {1}", match.PackageName, match.PackageVersionStr);
                        }
                    }


                    if (verbose)
                    {
                        Console.WriteLine("Selected package: {0} {1}   ({2})", highest.PackageName, highest.PackageVersionStr, highest.PackageVersion.ToString());
                    }

                    string versionStr = highest.PackageVersionStr;
                    if (exact == true)
                    {
                        versionStr = string.Format("[{0}]", highest.PackageVersionStr);
                    }

                    // find a list of all matching project files
                    Tuple<int, string[]> results = GetProjectFiles(projectFilespec, verbose);

                    retVal = results.Item1;

                    if (retVal != 0)
                    {
                        goto get_outta_dodge;
                    }

                    retVal = UpdateProjectFiles(results.Item2, versionStr, packageName, verbose, dryrun);
                    
                }
            }

            get_outta_dodge:


#if DEBUG
            Console.WriteLine("\nPress a key...");
            Console.ReadKey();
#endif
            return retVal;
        }


        string GetRawPackageList(string tag, string source)
        {
            string output;
            string errors;
            int exitCode = 0;

            string args = "";


            if ( String.IsNullOrEmpty(source) )
            {
                args = String.Format("list {0}", tag);
            }
            else
            {
                args = String.Format("list {0} -Source {1}", tag, source);
            }


            using (Process infoProc = new Process())
            {
                infoProc.StartInfo.FileName = "nuget.exe";
                infoProc.StartInfo.UseShellExecute = false;
                infoProc.StartInfo.RedirectStandardOutput = true;
                infoProc.StartInfo.RedirectStandardError = true;
                infoProc.StartInfo.Arguments = args;

                infoProc.Start();

                using (StreamReader stdout = infoProc.StandardOutput)
                {
                    using (StreamReader stderr = infoProc.StandardError)
                    {
                        // Display the results.
                        output = stdout.ReadToEnd();
                        errors = stderr.ReadToEnd();

                        // Clean up.
                        stderr.Close();
                        stdout.Close();
                    }
                }

                exitCode = infoProc.ExitCode;
                infoProc.Close();
            }

            if (exitCode != 0)
            {
                throw new Exception(errors);
            }

            return output;
        }




    }
}
