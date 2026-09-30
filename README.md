# Bouncing Circle

A Windows experiment app that shows a bouncing circle on one of two monitors.

It is a single `.exe` with nothing to install. It runs on the .NET Framework 4.x that comes with every copy of Windows 10 and 11.

## Run

Copy `dist/BouncingCircle.exe` to the Windows PC and double-click it.

1. **Monitor with the moving circle:** choose Monitor 1, Monitor 2 or Random.
   - Use the `Monitor 1 =` and `Monitor 2 =` lists to pick which physical display is which.
   - **Identify displays** shows a label on each screen so you can check the mapping.
   - If there are 3 or more displays, the default is the two non-primary (external) displays.
2. Set the circle speed (px/s), the circle diameter (px), the background gray level and the circle gray level. The defaults are a white background and a black circle.
3. Click **Start experiment**. Both experiment monitors fill with the background color, and the circle bounces on the chosen one.
4. Press **Esc**, **Space** or **Enter** to stop.
5. Click **Success** or **Fail** (you can also add notes). The row is then written to the log.
   - Tick **Relaunch immediately with the same settings** to start the next run straight away.
   - **Relaunch last** in the main window repeats the previous settings. In Random mode, the monitor is drawn again for each run.

## Log

The log is `experiments_log.csv`, saved next to the `.exe`. If that folder is read-only, it goes to `Documents\BouncingCircle\` instead. It has one row per experiment:

`experiment_id, start_time, end_time, duration_s, monitor_mode, monitor_used, display, speed_px_per_s, circle_diameter_px, background_gray, circle_gray, start_angle_deg, subject, result, notes`

`last_settings.ini` stores the most recent settings, so they are filled in again the next time you open the app.

## Notes

- The app runs per-monitor DPI-aware, so the speed is in real screen pixels even when Windows scaling is set to 125% or 150%.
- Motion is timed by the clock and synced to the display refresh, so the speed stays accurate even when a frame is dropped.
- To change the allowed value ranges, edit the constants at the top of `ExperimentSettings` in `src/BouncingCircle.cs`.

## Rebuild

- **On Windows:** run `build.bat`. It uses the C# compiler that is built into Windows.
- **On macOS with Mono:** run this from the repository root:

  ```bash
  R=/opt/homebrew/lib/mono/4.5-api; csc -nologo -noconfig -nostdlib -target:winexe -optimize+ -out:dist/BouncingCircle.exe -r:$R/mscorlib.dll -r:$R/System.dll -r:$R/System.Core.dll -r:$R/System.Drawing.dll -r:$R/System.Windows.Forms.dll src/BouncingCircle.cs
  ```
