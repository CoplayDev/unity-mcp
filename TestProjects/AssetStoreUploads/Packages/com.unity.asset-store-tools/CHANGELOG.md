# Changelog
All notable changes to this package will be documented in this file.

## [12.0.1] - 2025-01-16

### Preview Generator changes
- Updated generated preview collection UI to display the asset extension
- Fixed an issue with some prefab and model asset types not generating previews
- Fixed an error that could occur when changing scenes after deleting a preview source asset

## [12.0.0] - 2025-01-13

### General changes
- The code comprising the Asset Store Publishing Tools has been refactored.
- Added dependency on Newtonsoft Json

### Uploader changes
- Updated window to retain its state if closed unless a domain reload occurs
- Added option to generate higher resolution asset previews when exporting
- Fixed a rare issue where authentication would fail
- Minor UI tweaks

### Validator changes
- Added validation tests for:
    - Package naming
	- Project Template assets
- Updated the Type Namespace validation test to check for Unity top level namespaces

### Exporter changes
- Updated how asset previews are generated/included for the  package that is being exported

### Preview Generator
- Added a Preview Generator window that can be used to pre-generate and inspect package previews before exporting

## [11.4.4] - 2024-11-29

### Validator Changes
- The validator UI window description section can now be expanded or shrunk to take up less screen space
- Updated severity of the Model Importer Logs validation test

### Exporter Changes
- Updated exporter to always exclude hidden files and folders beginning with the dot symbol (e.g.: .hiddenFolder/ or .hiddenfile.txt)
- Updated exporter to explicitly exclude extended attribute files when exporting packages on macOS systems

### Other
- Moved the Asset Store Tools toolbar items into the Tools section
- Fixed several window-related compilation warnings when using Unity 6 versions of the Editor

## [11.4.3] - 2024-08-01

### Validator Changes
- Hotfix: Remove non-ascii characters from the demo scene validation

## [11.4.2] - 2024-07-30

### Validator Changes
- Check for nested .unitypackage files in the demo scene validation
- Prevent normal map test from erroring when misc importer types are detected
- Remove Templates category from the uncompressed images requirement list

## [11.4.1] - 2024-05-10

### Exporter Changes
- Fixed an issue with bundled plugin folder contents not being exported

### Other
- Miscellaneous internal changes

## [11.4.0] - 2024-01-23

### Uploader Changes
- Added prevention of uploading packages larger than 6 GB
- Added a prompt to allow automatically generating meta files within hidden folders
- Fixed some obsolete API usage warnings in newer Unity versions

### Validator Changes
- Added validation tests for:
    - Animation Clip take names
	- Model import logs
	- Uncompressed Package size
- Updated the fail severity of Audio Clipping validation test
- Updated the Demo Scene test to treat default scenes with custom skyboxes as valid demo scenes
- Fixed some obsolete API usage warnings in newer Unity versions

### Other
- Added an option to check for Asset Store Publishing Tools updates

## [11.3.1] - 2023-08-14

### Uploader Changes
- Added the option to select indirect package dependencies from the project (e.g. Mathematics package installed by the Burst package)

### Validator Changes
- Updated the Texture Dimensions test to ignore 'Sprite' and 'Editor GUI' texture types

### Exporter Changes
- Updated exporter to ignore the 'ProjectSettings/ProjectVersion.txt' asset when exporting 'Complete Project' category packages

## [11.3.0] - 2023-07-04

### Uploader Changes

- Added the option to validate a pre-exported package
- Added the option to export a .unitypackage file without uploading
- Updated the dependency selection UI

### Validator Changes

- Added the option to validate several asset paths at once
    - Note: when validating package that is comprised of several folders (e.g. Assets/MyPackage + 
	Assets/StreamingAssets + Assets/WebGLTemplates), please select all applicable paths that would be included in the package
- Added several new validation tests for:
    - File Menu Names
	- Compressed files 
	- Model Types
	- Texture Dimensions
	- Particle Systems
	- Normal Map Textures
    - Audio Clipping
    - Path Lengths
    - Script Compilation	
- Updated validation test severities based on package category
- Updated validation tests to each have their own test logic class
- Updated validation tests to be displayed in alphabetical order
- Fixed several issues with the namespace check test
- Fixed scenes in Samples~ folders not being taken into account for the sample scene check test
- Other internal changes

### Exporter Changes

- Package exporter is now a separate module (similar to Uploader and Validator)
- Fixed hidden folders being included when exporting package content
    - Note: this prevents an issue with the Unity Editor, where exported hidden folders would appear in the Project window 
	as empty folders when imported, despite having content on disk. Content nested within hidden folders is still collected, 
	provided it contains unique .meta files

## [11.2.2] - 2023-02-23

### Validator Changes

- Updated the 'LOD Setup' test to address some issues
	- Added additional checks for LOD renderers (inactive renderer check, LOD Group reference check, relative hierarchy position to LOD Group check)
	- LOD Group Component is no longer required to be on the root of the Prefab
	- Updated the test result message interface when invalid Prefabs are found

## [11.2.1] - 2023-01-17

### Uploader Changes

- Added a more informative error when exporting content with clashing guid meta files in hidden folders
- Fixed a compilation issue for Unity 2020.1 and 2020.2
- Fixed a rare error condition when queueing multiple package uploads in quick succession
- Fixed Asset Store Uploader state not being properly reset if the uploading process fails

### Validator Changes

- Updated the Asset Store Validator description
- Fixed a rare memory overflow issue when performing package validation

## [11.2.0] - 2022-11-03

### Uploader Changes

- Uploader will now use the custom package exporter by default
    - An option to use the legacy (native) exporter can be found in the Asset Store Publishing Tools' settings window
- When exporting from the Assets folder, package dependencies can now be selected individually instead of being a choice between 'All' or 'None'
    - This option is only available with the custom exporter
- Changed the way the Uploader reports completed uploading tasks
    - Modal pop-up has been replaced by a new UI view state
	- Added an option to the Asset Store Publishing Tools' Settings to display the pop-up after a completed upload
- Changed exported .unitypackage files to have distinguishable file names
- Fixed the Uploader window indefinitely stalling at 100% upload progress when a response from the Asset Store server is not received
- Fixed native package exporter producing broken packages when the export path contained hidden folders
- Fixed an issue with high CPU usage when uploading packages
- Fixed Asset Store Publishing Tools' settings not being saved between Editor sessions on macOS
- Other minor changes and tweaks

### Validator Changes

- Added two new tests:
    - 'Types have namespaces': checks whether scripts and native libraries under the validated path are nested under a namespace
	- 'Consistent line endings': checks whether scripts under the validated path have consistent line endings. This is similar to the warning from the Unity Editor compilation pipeline when a script contains both Windows and UNIX line endings.
- Improved 'Reset Prefabs' test to display and be more informative about prefabs with unusually low transform values
- Improved 'SpeedTree asset inclusion' test to search for '.st' files
- Improved 'Documentation inclusion' test to treat '.md' files as valid documentation files
- Improved 'Lossy audio file inclusion' test to treat '.aif' and '.aiff' files as valid non-lossy audio files
- Improved 'Lossy audio file inclusion' test to search the project for non-lossy variants of existing lossy audio files
- Removed 'Duplicate animation names' test
- Tweaked validation severities for several tests
- Other minor changes and tweaks

## [11.1.0] - 2022-09-14

### Uploader Changes

- Package Publisher Portal links can now be opened for all packages regardless of package status
- External Dependency Manager can now be selected as a 'Special Folder' if found in the root Assets folder

### Validator Changes

- Added category selection for the Validator
    - Categories help determine the outcome of package validation more accurately. For example, documentation is not crucial for art packages, but is required for tooling packages.
- Added a list of prefabs with missing mesh references to 'Meshes have Prefabs' test when the test fails
- Corrected the message for a passing 'Shader compilation errors' test
- Improved the floating point precision accuracy of 'Reset Prefabs' test
- Fixed 'Missing Components in Assets' test checking all project folders instead of only the set path
- Fixed 'Prefabs for meshes' test not checking meshes in certain paths
- Fixed 'Reset Prefabs' test failing because of Prefabs with a Rect Transform Component
- Fixed 'Reset Prefabs' test ignoring Transform rotation
- Fixed test description text overlapping in some cases
- Other minor changes and tweaks

## [11.0.2] - 2022-08-09

- Corrected some namespaces which were causing issues when deriving classes from Editor class

## [11.0.1] - 2022-08-05

### Uploader Changes

- Added Settings window (Asset Store Tools > Settings)
- Added Soft/Junction Symlink support (enable through Settings)
- Added workflow and path selection serialization (workflow saved locally, paths locally and online)
- No more logs when using the `-nullable` compiler option (thanks @alfish)
- Some API refactoring in preparation for CLI support
- Other minor fixes/improvements

**Note:** when updating Asset Store Tools from the Package Manager, don't forget to remove the old version from the project (V11.0.0) before importing the new one (V11.0.1)


## [11.0.0] - 2022-07-20

### Uploader changes

- UI has been reworked using UI Toolkit
- New login window, allowing to login using Unity Cloud Services
- Improved top bar, including search and sorting
- Draft packages moved to the top
- Added category, size, and last modified date next to the package
- Added a link to the publishing portal next to the package
- New uploading flow: “Pre-exported .unitypackage”
- Previous uploading flow (folder selection) has been renamed to “From Assets Folder”
- Dependencies check has been renamed to “Include Package Manifest” for clarity
- Special Folders can now be selected and uploaded together with the package’s main folder (i.e. StreamingAssets, Plugins)
- You can now upload to multiple packages at the same time without waiting for the first one to finish
- Package can now be validated in the Uploading window by pressing the “Validate” button
- Added refresh and logout buttons to the bottom toolbar for easier access
- Packages caching - package information will no longer be redownloaded every time you open the Uploader window during the same Editor session
- (Experimental) Custom exporter - will export your package ~2 times faster, but may miss some asset previews in the final product. To enable it - click three dots on the top left side of the window and enable “Use Custom Exporting”


### Validator changes

- UI has been reworked using UI Toolkit
- New tests based on the new guidelines
- Updated tests’ titles, descriptions, and error reporting

## [5.0.5] - 2021-11-04

- Fixed namespace issues

## [5.0.4] - 2020-07-28

- Fixed issues with Unity 2020.1

## [5.0.3] - 2020-05-07

- Remove "Remove Standard Assets" check

## [5.0.2] - 2020-04-21 

- Enable auto login with Unity account
- Upload package with thread

## [5.0.1] - 2020-03-23

- Fix domain resolve issue

## [5.0.0] - 2019-10-09

- Added "Package Validator" tool
- Added Help window
- Added logout confirmation popup
- Updated toolbar menu layout
- Removed "Mass Labeler" tool
- Updated layout of Login and Package Upload windows
- Error messages are now more elaborate and user-friendly
- Removed deprecated "Main Assets" step from the Package Upload window
- Package Upload window now has a step for including package manager dependencies
- Tooltips are now added to each upload process step


## [4.1.0] - 2018-05-14

- Made Tool compatible with 2017.1

## [4.0.7] - 2017-07-10

- Tweaked menu items.

## [4.0.6] - 2016-07-15

- Improved error messages.

## [4.0.5] - 2016-03-17

- Enabling upload of fbm files.

## [4.0.4] - 2015-11-16

- Login improvements

## [4.0.3] - 2015-11-16

- Prepare the Tools for Unity 5.3

## [4.0.2] - 2015-10-23

- Fixed issue where Upload button would not work for some projects.
- Fixed issues for publishers that only had one package.

## [4.0.0] - 2015-09-01

- Replaced Package Manager with Package Upload. Package management is now handled by Publisher Administration