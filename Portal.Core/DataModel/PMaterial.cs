﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Portal.Core.DataModel
{
    public class PMaterial
    {
        public string Name { get; set; }
        public string DiffuseColor { get; set; }
        public List<PTexture> Textures { get; set; }

        public PMaterial(string name, PColor color)
        {
            Name = name;
            DiffuseColor = color.ToHex();
        }

        public PMaterial(string name, PColor color, List<PTexture> textures)
        {
            Name = name;
            DiffuseColor = color.ToHex();
            Textures = textures;
        }
    }
}
