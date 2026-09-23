# Platform availability

Saved display filters can combine tags with a platform condition. For example, create **Waiting for Linux**, choose **Linux**, and select **No build found**. No tags are required for a platform-only filter.

- **Build available** means Quiver recognizes a downloadable build for the selected OS.
- **No build found** means the latest checked release has recognizable builds for other platforms, but none for the selected OS.
- **Availability unknown** covers unchecked projects, manually managed apps, and releases whose assets cannot be classified.

These conditions use the project's latest release, even when an older version is pinned for installation. They respect the app's asset filter. They do not indicate tested runtime compatibility or support through Wine. Failed checks retain previously verified results.

In a catalog, **New platform support** appears when a previously checked game gains a build for a selected OS. It follows the catalog's platform filter, which defaults to your device's OS. Choose **All platforms** to see other platform additions. Cards show **Recently available for Linux** (or the relevant OS); release and detection details are in the tooltip. **Dismiss** is alongside the usual card actions, or in **More** when needed. **Dismiss visible notices** clears the currently displayed additions without accepting catalog edits or dismissing notices for other platforms.

The first check establishes a baseline. It does not announce existing support as new, and detection dates are not upstream release dates. History stays on this device and persists between launches. Automatic platform information never changes your editable tags or installation pins.
