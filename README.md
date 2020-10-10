# update_package_reference #

## The (long winded) Problem Statement ##

Control SW Applications currently consume several libraries as source code mapped into the solution structure.  When the libraries are modified, CI picks up on the change and rebuilds the consuming application with the latest version of the source code.  The library version numbers don't matter.

When transitioning these libraries to NuGet, the consuming applications will install a specific library version.  Some clients outside of Control SW Engineering will likey perfer stable library builds, but Extron's Control applications will still want to track with the latest version.

NuGet features a CLI command that will update a project's packages, but these utilities will work only with packages.config type references.  Control SW Eng is using PackageReference, and the NuGet team will not modify the NuGet tools to work with PackageReference.

For PackageReference, the NuGet team would prefer developers use floating version numbers for their packages, but business rules such as "Nearest Wins" complicate this workflow - in certain cases package downgrades are possible.  So without a workable NuGet CLI tool the only other tool available is the Update-Package command, which is available only from within Visual Studio's Package Manager Console.

If developers make a change to a library, there is no way to know when which NuGet package will contain the changes.  Even if such tracking were possible, someone would then have to manually open and update the affected applications, because the Update-Package command can't be scripted.

The best option then is for CI to modify the affected projects.  There are two options:

- When a library is modified, CI will update the project files of the affected applications.  This is a bad idea - for any given project it would require CI to maintain a list of latest-tracking applications.

- When an affected application is built, it will have it's package references updated in a pre-build step.  **This is where update_package_reference comes in.**
