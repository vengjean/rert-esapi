# ReRT script

## Adopting ReRT at another institution

ReRT is configurable for any institution. To set up:

1. Clone this repo.
2. Copy `Configuration/InstitutionConfig.sample.yaml` to
   `Configuration/InstitutionConfig.local.yaml` and edit the values
   (institution name, department header, output paths). The `.local`
   file is gitignored.
3. (Optional) Drop your logo at the `logoPath` you specified, or leave
   `logoPath` empty to skip the logo. A common convention is
   `Configuration/Logo.local.png` — also gitignored.
4. Review `Configuration/ReRTConfig.yaml` — these are clinical
   constraint sets shipped with the project (Stanford SABR, UMichigan
   re-irradiation). Add or override constraints for your local practice
   in additional YAML files.
5. Open `ReRT.sln` in Visual Studio, restore NuGet packages, and build
   against your ESAPI assemblies.

If `InstitutionConfig.local.yaml` is absent, the loader falls back to
`InstitutionConfig.sample.yaml` so a fresh clone still produces a
working run (no logo, generic department header, single default output
path under `C:\ReRT_Reports`).

## Setup

To use the script, you must compile it on your system. You should be able to open the project with Visual Studio 2022 Community Edition. Open the .sln file.
The script was developed for Eclipse version 18.1. It may not work with other versions of Eclipse or Varian ESAPI.

1. You will need to restore all NuGet packages for compilation. This may require cleaning the solution and restarting.
2. You may need to relink references to the Varian dlls.
3. Compile as Release for x64.

## How to use the script

Before your first run:

1. Review `Configuration/ReRTConfig.yaml` in the Configuration folder, and add any default assignments of alpha/beta that are appropriate for your institution (see the `defaults:` block).
2. Approve the script in Eclipse (it is a write-enabled script)

ReRT offers two dose-accumulation methods, selected with the **dose accumulation
method** toggle in the top bar:

- **Rigid registration** — accumulates the spatially-transformed dose from each
  prior plan into an EQD2 (and optionally physical) plan sum.
- **Conservative max dose** — sums each prior plan's own near-max (D0.1cc) EQD2
  within an anisotropic expansion (in-plane / superior-inferior) around a chosen
  target structure — or over the whole structure in global mode — a conservative
  worst case that assumes the per-plan hotspots coincide. The target is mapped into
  each plan through the registration and the near-max is computed from the plan's
  dose voxels, with boundary voxels partial-volume weighted to track Eclipse's DVH.

**This program comes with absolutely no guarantees of any kind.**

```
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## LICENSE

Copyright (c) 2025-2026 Veng Jean Heng. Published under the [MIT license](LICENSE).

Portions of ReRT are derived from
[DoseConverter](https://github.com/NickChng/DoseConverter), Copyright (c) 2021
Denis Brojan, also MIT licensed. Its copyright notice is retained in
[LICENSE](LICENSE) and must stay with any copy or substantial portion of the code.
