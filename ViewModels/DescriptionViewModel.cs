// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT
// Portions derived from DoseConverter (https://github.com/NickChng/DoseConverter),
// Copyright (c) 2021 Denis Brojan, MIT License.

namespace ReRT
{
    public class DescriptionViewModel
    {
        public string Id { get; set; }
        public string Description { get; set; } = "Default Description";

        public DescriptionViewModel(string id, string description)
        {
            Id = id;
            Description = description;
        }
        public DescriptionViewModel(OnlineHelpDefinitionsDefinition definition)
        {
            if (definition != null)
            {
                Id = definition.DefinitionId;
                Description = definition.Text;
            }
            else
            {
                Description = "No online help";
            }
        }

    }
}
