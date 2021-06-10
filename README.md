# update_package_reference #

[Revision History](#Revision-History)

[Problem statement](#The-(long-winded)-Problem-Statement)

[Who is this for](#Who-is-this-for)

[Requirements](#Requirements)

[Using update_package_reference](#Using-update_package_reference)

[Search Tags](#Search-Tags)

---

---

## Revision History ##

|Version|Notes|
|---|---|
| v1.3.0.0 | Added two new options: **r** or **readversion** to see which versions of a package are being referenced and **n** or **normalize** to make all projects reference the highest reference amongst all specified project files. |
| v1.2.0.0 | A new **e** or **explicit** parameter can be used to specify an exact version number or version range.  Also added **exact** option for discovered package versions. |
| v1.1.0.0 | Now supports .NET Standard projects.  Tags are now optional - if no tag is provided the package ID is used for the tag.  The utility was doing case sensitive searches against packages returned by Nuget/Artifactory, resulting in a No Packages Found error if casing wasn't exact.  This has been fixed.  |
| v1.0.0.0 | Initial Release. |

---

## The (long winded) Problem Statement ##

---

Control SW Applications currently consume several libraries as source code mapped into the solution structure.  When the libraries are modified, CI picks up on the change and rebuilds the consuming application with the latest version of the source code.  The library version numbers don't matter.

When transitioning these libraries to NuGet, the consuming applications will install a specific library version.  Some clients outside of Control SW Engineering will likey perfer stable library builds, but Extron's Control applications will still want to track with the latest version.

NuGet features a CLI command that will update a project's packages, but these utilities will work only with packages.config type references.  Control SW Eng is using PackageReference, and the NuGet team will not modify the NuGet tools to work with PackageReference.

For PackageReference, the NuGet team would prefer developers use floating version numbers for their packages, but business rules such as "Nearest Wins" complicate this workflow - in certain cases package downgrades are possible.  So without a workable NuGet CLI tool the only other tool available is the Update-Package command, which is available only from within Visual Studio's Package Manager Console.

If developers make a change to a library, there is no way to know when which NuGet package will contain the changes.  Even if such tracking were possible, someone would then have to manually open and update the affected applications, because the Update-Package command can't be scripted.

The best option then is for CI to modify the affected projects.  There are two options:

- When a library is modified, CI will update the project files of the affected applications.  This is a bad idea - for any given project it would require CI to maintain a list of latest-tracking applications.

- When an affected application is built, it will have it's package references updated in a pre-build step.  **This is where update_package_reference comes in.**

## Who is this for ##

---

This utility was created for CI as a tool to allow applications to track the latest versions of the library packages they depend on.  The CI process, having an understanding of which package references must be kept up to date, will use this tool for those packages.

For packages that aren't to be automatically updated, it's up to developers to make package changes, and for that task the package manager in Visual Studio is a better tool.

**update_package_reference** is intended for updating references to Extron libraries hosted on Artifactory.  It could be used, by CI or developers, to update the references to any NuGet package, Extron or 3rd party, but one would have to be mindful of a 3rd party package's dependency tree.

This utility will only work on project that use PackageReference for NuGet resources.  It will not work for Packages.Congig - for that the NuGet CLI should be used.

## Requirements ##

---

**update_package_reference** leverages the NuGet CLI.  The NuGet CLI must be installed on the machine running **update_package_reference**, and the NuGet CLI's installation folder must appear in the environment's search path variable.

When sources are added to NuGet and credentials bound to the NuGet CLI, the username applied at that time need to be/need to have been a fully qualified username - **user@extron.com** ratehr than simply **user**.  If improper usernames were used users might encounter Username + Password prompts when running **update_package_reference**

Extron Artifactory users should be set up to work with Virtual Repositories as a standard practice.  Neither the NuGet CLI nor **update_package_reference** recognize Virtual Repository aliases.  To use **update_package_reference** a NuGet source that points to the "local" (concrete) repository must be added (this being in addition to, not a substitute for virtual repository sources)

## Using update_package_reference ##

---

The Package Manager Console **Update-Package** command accepts the ID (name) of a package and modify's any the current solution's projects that reference that package to make use of the latest version.  **update_package_reference** works in much the same way, but it doesn't have the benefit of loaded solution and project files so it accepts parameters that help it along.

|shorthand|name|description|
|---|---|---|
| c | csproj | The project file(s) that will have their references to the specified package updated.  Wildcards are supported.  Only project files with **PackageReference**s that include the named package will be modified. |
| p | package | The ID or name of a package to be upgraded.   |
| t | tag | A search word to help narrow/speed up the package search.  Can match the package, description or something ftom the package's list of tags. See: [Search Tags](#Search-Tags)|
| s | source | (optional) The URI of a NuGet repository such as [https://extron.jfrog.io/artifactory/nuget-dev/](https://extron.jfrog.io/artifactory/nuget-dev/) |
| d | dryrun | (optional) If this parameter is supplied project files are not modified.  **update_package_reference** will instead report what changes would have been made. |
| v | verbose | (optional) This parameter puts **update_package_reference** into verbose mode. |
| x | exact | (optional) If exact is specified, projects are updated to accept only the exact highest version found in the online search - no higher or lower versions will be accepted (i.e. "[x.y.z]" instead of "x.y.z" ) |
| e | explicit | (optional) Allows explicit version number references or version ranges to be provided.  If an explicit version is provided, **tag**, **source** and **exact** are ignored, and no online package search is attempted. |
| r | readversion | (optional) outputs the package version number referenced by the specified project file(s).  Im multiple project files are specified/implied, the highest/most recent version number is noted.  **dryrun**, **exact**, **explicit**, **source**, and **tag** are ignored as no project changes are made, and no online search is performed. |
| n | normalize | (optional) determines the highest/most recent version of a package in the specified project file(s) and modified all project files to reference that version. **tag**, **source** and **exact** are ignored if provided. |

**Example 1**: - do a practice run of updating all projects in GCPro to use the latest version of the extron.pro.communication.control package:

```bash
update_package_reference -c c:\projects\control\gcpro\*.csproj -p extron.pro.communication.control -t global_messaging -d -verbose
```

**Example 2**: - do a practice run of updating all projects in GCPro to use _only_ the latest version of the extron.pro.communication.control package:

```bash
update_package_reference -c c:\projects\control\gcpro\*.csproj -p extron.pro.communication.control -t global_messaging -d -verbose -x
```

**Example 3**: - do a practice run of updating all projects in GCPro to use _only_ an explictly provided version.  No online package search is made - typos and bad version references are on you :)

```bash
update_package_reference -c c:\projects\control\gcpro\*.csproj -p extron.pro.communication.control -d -verbose -e [1.2.3]
```

**Example 4**: - same as Example 3, where a version range is provided.  In this case, everything from version 3.0 up to and **excluding** version 4.0.

```bash
update_package_reference -c c:\projects\control\gcpro\*.csproj -p extron.pro.communication.control -d -verbose -e [3.0,4.0)
```

As long as you include the -d option these are safe to try.  Changes are made only if this option is omitted.

### Return Codes ###

| Code | Description |
|---|---|
| 0 | Success/no errors.  (projects that don't contain a matching ProjectReference are not considered to be an error) |
| 1 | Command line parse error |
| 2 | No matching packages found in the Repo (bad search tag?) |
| 3 | No matching project files found |
| 4 | Failed to update one or more project files (file locked, read-only or ???) |
| 5 | Error while scanning specified folder for project files |

## Search Tags ##

---

update_package_reference leverages the NuGet CLI to obtain a list of packages.  If told to list everything, NuGet will spend a long time streaming out a list of packages.  The search can be narrowed by searching for a specific tag.  A tag is a string/keyword appearing in a package name, a packages list of tags (specified in the package nuspec file), or package description (also in the nuspec file).

> using a package name for the tag can produce unfavorable results - often times the name is not recognized and nuget will go into a painfully lengthy package listing process as if told to list everything.

### Important notes about tags ###

#### Search Tags are not case sensitive ####

Search Tags are not case sensitive.  If a package contains the tag **Nortxe**, providing a search tag value of **nortxe** should locate it.

#### Avoid camel or pascal case search tags ####

For NuGet tag searches, one should avoid camel or pascal case search tags.  It appears the tag will first be applied to the search as provided, then broken up into capitalized tokens applied to individual searches.

For example, when searching for a package containing the tag **NORTXEAbZy**, a search tag of **NORTXEAbZy** will yield the following internal results:

```bash
Blog.Corezy.Webapi.Template 1.0.0
HiNetCloud.System.Web.WebPages.Razor 20.6.29.11
MonoGame.Framework.Bridge 3.7.0.2
MonoGame.Template.Gtk.CSharp 3.8.0.5
Muffin.Encryption.Service 3.33.333.9020
NumberProgressBar 1.1.2
ZY.BaseHelper 1.0.1.2
ZY.Common 1.0.22
ZY.EncryptKey 1.0.0
ZY.Extend 1.0.0.3
ZY.Extend.Core 1.0.0
ZY.FrameWork 1.0.0
ZY.Framework.Core 1.0.3.1
ZY.HTTP 1.0.0.7
ZY.IOC 1.0.0
ZY.IOC.Mvc 1.0.0
ZY.Logger 1.0.5
ZY.Office 1.0.0.2
ZY.ORM 1.0.2.9
ZY.ORM.Extend 1.0.0.2
ZY.PollyExtend 1.0.0.1
ZY.Template.Core 1.0.0.4
ZY.WeChat.PublicPlatform 1.0.0
ZyBlog.Core.Webapi.Template 1.1.0
zyCal 1.8.0
zyCATApi.Template.Core 1.2.0
zyWebApiTemplate.Core 1.0.0
```

...where a search tag of **nortxeabzy** yields only the sought-after package:

```bash
Muffin.Encryption.Service 3.33.333.9020
```

#### Avoid tags with underscores, dashes or periods/dots ####

If a package contains a tag with underscores, dashes or the like, it's impossible to craft a search tag that won't match more packages than desired (see above).

#### Description tags are best for CI ####

When a NuGet package is published to Artifactory, a search tag can be used to immediately that package if there is a matching string in the package description.

Search tags are also applied to a package's tag collection, but package tags appear to be indexed by the repo server, and this indexing process doesn't happen immediately.  Where CI builds and publishes a library package and then builds dependant applications expecting that they will be updated to the latest version (using this utility), those builds will likely include an older version of the package unless the search tag appears in the package description.

[end]
