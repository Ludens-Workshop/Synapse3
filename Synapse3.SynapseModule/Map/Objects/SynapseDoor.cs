﻿using System.Collections.Generic;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using Mirror;
using Synapse3.SynapseModule.Map.Schematic;
using UnityEngine;

namespace Synapse3.SynapseModule.Map.Objects;

public class SynapseDoor : NetworkSynapseObject
{
    public static Dictionary<SpawnableDoorType, BreakableDoor> Prefab { get; } = new Dictionary<SpawnableDoorType, BreakableDoor>();

    public DoorVariant Variant { get; }
    
    public override GameObject GameObject => Variant.gameObject;
    
    public override NetworkIdentity NetworkIdentity => Variant.netIdentity;
    
    public override ObjectType Type => ObjectType.Door;

    public override void OnDestroy()
    {
        Map._synapseDoors.Remove(this);
        base.OnDestroy();
    }

    private string _name;

    public string Name
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_name)) return GameObject.name;
            return _name;
        }
    }

    public bool Open
    {
        get => Variant.IsConsideredOpen();
        set => Variant.NetworkTargetState = value;
    }

    public bool Locked
    {
        get => Variant.ActiveLocks > 0;
        set => Variant.ServerChangeLock(DoorLockReason.SpecialDoorFeature, value);
    }

    public DoorPermissions DoorPermissions
    {
        get => Variant.RequiredPermissions;
        set => Variant.RequiredPermissions = value;
    }

    public bool IsBreakable => Variant is BreakableDoor;

    public bool IsDestroyed => Variant is BreakableDoor { IsDestroyed: true };

    public bool IsPryable => Variant is PryableDoor;

    public bool TryBreakDoor()
    {
        if (Variant is BreakableDoor breakableDoor)
        {
            breakableDoor.IsDestroyed = true;
            return true;
        }

        return false;
    }

    public bool TryPry()
    {
        if (Variant is PryableDoor pryableDoor)
            return pryableDoor.TryPryGate();

        return false;
    }

    public override string ToString() => Name;


    public SynapseDoor(SpawnableDoorType type, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Variant = CreateDoor(type, position, rotation, scale);
        SetUp();
    }
    
    internal SynapseDoor(DoorVariant variant)
    {
        Variant = variant;
        SetUp();
    }
    
    /*internal SynapseDoor(SynapseSchematic.DoorConfiguration configuration)
    {
        OriginalScale = configuration.Scale;
            CustomAttributes = configuration.CustomAttributes;
            UpdateEveryFrame = configuration.UpdateEveryFrame;
            DoorType = configuration.DoorType;
            
        OriginalScale = configuration.Scale;
        CustomAttributes = configuration.CustomAttributes;
        UpdateEveryFrame = configuration.UpdateEveryFrame;
        DoorType = configuration.DoorType;
        
        SetUp();
    }*/

    private void SetUp()
    {
        Map._synapseDoors.Add(this);
        var comp = GameObject.AddComponent<SynapseObjectScript>();
        comp.Object = this;
        if (Variant.TryGetComponent<DoorNametagExtension>(out var nametag))
            _name = nametag.GetName;
    }
    
    private DoorVariant CreateDoor(SpawnableDoorType type, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        return CreateNetworkObject(Prefab[type], position, rotation, scale);
    }
    
    public enum SpawnableDoorType
    {
        LCZ,
        HCZ,
        EZ
    }
}