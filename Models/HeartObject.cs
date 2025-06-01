using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheAdventure.Models;
using Silk.NET.SDL;

namespace TheAdventure.Models;

public class HeartObject : RenderableGameObject
{
    public HeartObject(SpriteSheet spriteSheet, (int X, int Y) position)
        : base(spriteSheet, position) { }
}
