# Paris Heat Trail – .NET MAUI location heat map

A .NET MAUI app that records the user's position into a local **SQLite** database and shows
the stored positions as a **heat map** drawn on top of the **.NET MAUI Maps** control.
The app ships with generated demo data (sight-seeing routes around Paris) so it shows a result
on the very first launch.

## Features

| Feature | Where |
|---|---|
| Record the device position every 5 s (min. 5 m apart) | `Services/LocationTrackingService.cs` |
| Persist positions in SQLite (`sqlite-net-pcl`) | `Services/LocationRepository.cs`, `Models/LocationPoint.cs` |
| Heat map built by extending the MAUI `Map` | `Controls/HeatMap.cs` |
| Deterministic Paris sample data + landmark pins | `Services/SampleDataSeeder.cs` |
| MVVM page (CommunityToolkit.Mvvm) | `ViewModels/MainViewModel.cs`, `Views/MainPage.xaml` |
| Android manifest (permissions + Maps key) | `Platforms/Android/AndroidManifest.xml` |

## How the heat map works

`HeatMap` derives from `Microsoft.Maui.Controls.Maps.Map` and adds a bindable `HeatPoints` property.

1. Points are binned into a grid whose cell size follows the zoom level (~34 cells across the screen).
2. Each cell's point count is normalised on a log scale to an intensity between 0 and 1.
3. Every cell is drawn as three concentric translucent `Circle` map elements coloured from a
   blue → cyan → green → yellow → red gradient. Overlapping circles add up, giving the glow.

Only the cross-platform Maps API is used, so the same code runs on Android, iOS and Mac Catalyst.

## Requirements

* .NET 10 SDK with the MAUI workload (`dotnet workload install maui`)
* Android SDK + JDK 17/21 (or Xcode on a Mac for iOS / Mac Catalyst)
* **Android only:** a Google Maps API key (*Maps SDK for Android* enabled). Put it into
  `Platforms/Android/AndroidManifest.xml` replacing `YOUR_GOOGLE_MAPS_API_KEY`.
  Without a key the map tiles stay blank – the heat layer and pins still render.
* iOS / Mac Catalyst use Apple Maps and need no key.

> The Maps control has no Windows implementation, so there is no Windows target.

## Run

```bash
dotnet build -f net10.0-android
dotnet build -f net10.0-android -t:Run          # with an emulator/device attached
```

To test live tracking in the Android emulator use *Extended controls → Location* (set a route or a point).

## Project layout

```
Controls/      HeatMap – the extended Map control
Models/        LocationPoint (SQLite table), Landmark
Services/      Repository, tracking service, sample data seeder
ViewModels/    MainViewModel
Views/         MainPage (XAML + code-behind)
Platforms/     Android / iOS / Mac Catalyst manifests and entry points
```
