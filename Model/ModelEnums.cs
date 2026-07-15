// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

using System.ComponentModel;

namespace ReRT
{
    public enum ScriptStatus
    {
        [Description("Incomplete")] Incomplete,
        [Description("Complete")] Complete,
        [Description("Error")] Error,
        [Description("Warning")] Warning
    }
}
