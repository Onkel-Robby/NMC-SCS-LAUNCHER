# Third-party notices

Existing DecryptTruck notices remain in `third-party/decrypt-truck`.

## CityHash64 primitive

`src/NmcScsLauncher.Infrastructure/ThirdParty/ScsCityHash.cs` contains the independently
MIT-licensed CityHash64 primitive from:

https://github.com/sk-zk/TruckLib.HashFs/blob/c064f1d37fd75ba9a5f6bbbd1ec6df8a30b142ac/TruckLib.HashFs/CityHash.cs

Only this file, whose own header explicitly grants the MIT license, is included.
The GPL-licensed TruckLib.HashFs archive reader and TruckLib.Sii packages are not included.
The primitive's namespace and class visibility/name were changed for this application.
Its complete notices are preserved in the source and reproduced below.

Copyright (c) 2018, Dario Wouters

Copyright (c) 2011-2012, Alexander Nusov

Copyright (c) 2011 Google, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

The bounded HashFS definition reader is an independent implementation of the
archive data layout, using .NET compression APIs. Format research references:

- https://modding.scssoft.com/wiki/Documentation/Tools/Game_Archive_Packer
- https://github.com/nautofon/Archive-SCS
- https://github.com/Wilps93/scs_tools/blob/main/FORMAT.md
- https://github.com/sk-zk/Extractor/releases (64-bit v1 table-offset correction)

Research documentation is not a source-code dependency. No game assets are distributed.
