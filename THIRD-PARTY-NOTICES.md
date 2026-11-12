# Third-party notices

TrafficLimiter itself is MIT licensed (see `LICENSE`). It uses the following third-party components.

## WinDivert 2.2.2

- Packet interception driver and library: <https://github.com/basil00/WinDivert>
- Files used, **unmodified** from the official release `WinDivert-2.2.2-A`:
  `TrafficLimiter.Core/native/WinDivert.dll` and `TrafficLimiter.Core/native/WinDivert64.sys`
- License: dual-licensed under your choice of the **GNU LGPL v3** or the **GNU GPL v2**.
  The full text is in [`licenses/WinDivert-LICENSE.txt`](licenses/WinDivert-LICENSE.txt).
- TrafficLimiter links to `WinDivert.dll` dynamically (P/Invoke). The two files are embedded in the executable
  and extracted at runtime to `%LocalAppData%\TrafficLimiter\native\2.2.2\`; you can replace them there with
  another build of WinDivert 2.2.x. The complete source of WinDivert is available at the link above.

## CommunityToolkit.Mvvm

- MVVM source generators and helpers: <https://github.com/CommunityToolkit/dotnet>
- License: MIT, Copyright (c) .NET Foundation and Contributors.
