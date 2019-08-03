﻿using System.Collections.Generic;
using System.Linq;
using Helion.Util.Container.Linkable;
using Helion.World.Entities;
using static Helion.Util.Assertion.Assert;

namespace Helion.Maps.Geometry
{
    public class Sector
    {
        public readonly int Id;
        public readonly List<Side> Sides = new List<Side>();
        public readonly List<SectorFlat> Flats = new List<SectorFlat>();
        public readonly LinkableList<Entity> Entities = new LinkableList<Entity>();
        public byte LightLevel;
        public int Tag;

        public SectorFlat Floor => Flats[0];
        public SectorFlat Ceiling => Flats[1];
        public float UnitLightLevel => LightLevel / 255.0f;

        public Sector(int id, byte lightLevel, SectorFlat floor, SectorFlat ceiling, int special, int tag)
        {
            Precondition(floor.Z <= ceiling.Z, "Sector floor is above the ceiling");

            Id = id;
            LightLevel = lightLevel;
            Tag = tag;
            
            Flats.Add(floor);
            Flats.Add(ceiling);
            Flats.ForEach(flat => flat.SetSector(this));
        }

        public void Add(Side side)
        {
            Precondition(Sides.All(s => s.Id != side.Id), "Trying to add the same side twice");

            Sides.Add(side);
        }

        public LinkableNode<Entity> Link(Entity entity)
        {
            // TODO: Precondition to assert the entity is in only once.
            
            return Entities.Add(entity);            
        }

        public override bool Equals(object obj) => obj is Sector sector && Id == sector.Id;

        public override int GetHashCode() => Id.GetHashCode();
    }
}