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

        [ObfuscationAttribute(Exclude = true, ApplyToMembers = true)]
        internal class FWUpdateCLIOptions
        {
            [Option('v', "verbose", Required = false, Default = false, HelpText = "Set output to verbose messages (if omitted verbose mode is off).")]
            public bool Verbose { get; set; }

            [Option('p', "package", Required = true, HelpText = "The name of the package that will get an updated reference in the target project files.  tag is used to pare down a repo listing to a collection that includes this value.")]
            public string PackageID { get; set; }

            [Option('t', "tag", Required = true, HelpText = "A string that will be used to help find the package to be upgraded.  A tag can be a name or a value contained within a package tags collection. tag is used to narrow down the repo listing to as few entries as possible in addition to the desired package name.")]
            public string Tag { get; set; }

            [Option('s', "source", Required = false, Default = "", HelpText = "A URI of a source to browse for the desired package.")]
            public string Source { get; set; }

            [Option('c', "csproj", Required = true, HelpText = "The project file(s) to receive the updated Package Reference.")]
            public string ProjectFilespec { get; set; }

            [Option('d', "dryrun", Required = false, Default = false, HelpText = "Prevents the utility from making any proejct file changes.  Everything up to that point procedes normally.")]
            public bool DryRun { get; set; }
        }

        void HandleParseError(IEnumerable<Error> errs)
        {
            if (errs.Any(x => x is HelpRequestedError)) //|| x is VersionRequestedError))
            {
                Console.WriteLine("Example:\n");
                Console.WriteLine(@"    update_package_reference -t Keyword -p My.Nuget.Package -c c:\projects\Core\MyProj\*.csproj -s https://extron.jfrog.io/extron/api/nuget/nuget-test -v -d");
                Console.WriteLine("");
            }
            parseErr = true;
        }

        class MatchingPackage
        {
            string verStr;
            Version ver;

            public MatchingPackage() { verStr = ""; ver = new Version(); }

            public string PackageName { set; get; }
            public string PackageVersionStr
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

            public Version PackageVersion
            {
                get
                {
                    return ver;
                }
            }

        }

        public int Run(string[] args)
        {
            int retVal = 0;

            bool verbose = false;
            bool dryrun = false;
            string searchTag = "";
            string packageName = "";
            string packageNameLC = "";
            string projectFilespec = "";
            string sourceRepo = "";

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

                   }).WithNotParsed(HandleParseError);

            if (parseErr == true)
            {
                retVal = 1; // param error
            }
            else
            {
                if (verbose)
                {
                    Console.Write("Searching for package {0} with tag {1}");
                    if ( String.IsNullOrEmpty(sourceRepo) == false)
                    {
                        Console.Write(", looking in {0}", sourceRepo);
                    }
                    Console.Write("\n");
                }

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

                List<MatchingPackage> foundPackages = new List<MatchingPackage>();

                string packageRegex = String.Format("(?<pkgStr>{0})\\s+(?<verStr>\\d+(\\.\\d+)+)", packageName.Replace(".", "\\."));
                Regex reggie = new Regex(packageRegex);
                MatchCollection found = reggie.Matches(packageList);
                foreach ( Match entry in found )
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
                        continue;
                    }
                }

                if (foundPackages.Count == 0)
                {
                    Console.WriteLine("No packages found matching {0}", packageName);
                    retVal = 2; // no matches found
                }
                else
                {
                    if (verbose)
                    {
                        Console.WriteLine("Found {0} matching packages", packageName);
                    }

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

                    string rootFolder = Path.GetDirectoryName(projectFilespec);
                    string filespec = Path.GetFileName(projectFilespec);

                    if (verbose)
                    {
                        Console.WriteLine("Searching for {0} in {1}", filespec, rootFolder);
                    }

                    string[] projectFiles = Directory.GetFiles(rootFolder, filespec, SearchOption.AllDirectories);

                    if (projectFiles.Count() == 0 )
                    {
                        retVal = 3; // no project files to modify
                    }
                    else
                    {
                        if (verbose)
                        {
                            foreach (string projFile in projectFiles)
                            {
                                Console.WriteLine("    {0}", projFile);
                            }
                        }

                        string pattern = String.Format("<PackageReference Include=\"{0}\">\\s+(<Version>)(?<verRef>[\\[(]?[\\d.,*]+[\\])]?)(<\\/Version>)\\s+<\\/PackageReference>", packageName.Replace(".", "\\."));
                        Regex projPattern = new Regex(pattern);

                        foreach (string projFile in projectFiles)
                        {
                            string stuff = File.ReadAllText(projFile);
                            Match pkgRef = projPattern.Match(stuff);
                            if ( pkgRef.Success )
                            {
                                string newProjectFile = stuff.Substring(0, pkgRef.Groups["verRef"].Index);
                                newProjectFile += highest.PackageVersionStr; // Using the string instead of rendering the version object.
                                newProjectFile += stuff.Substring(pkgRef.Groups["verRef"].Index + pkgRef.Groups["verRef"].Value.Length);

                                if (dryrun)
                                {
                                    Console.WriteLine("Dry Run: Would have updated {0} PackageReference of {1} from {2} to {3}.  File not changed.", projFile, packageName, pkgRef.Groups["verRef"].Value, highest.PackageVersionStr);
                                }
                                else
                                {
                                    Console.WriteLine("Updating {0} PackageReference of {1} from {2} to {3}.", projFile, packageName, pkgRef.Groups["verRef"].Value, highest.PackageVersionStr);
                                    try
                                    {
                                        File.WriteAllText(projFile, newProjectFile);
                                    }
                                    catch
                                    {
                                        Console.WriteLine("    Failed to update {0}.  File locked or read-only?", projFile);
                                        retVal = 4;
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("No matching reference found in {0}.  File not changed.", projFile);
                                retVal = 5;
                            }
                        }
                    }
                }
            }

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
