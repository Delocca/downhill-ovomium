using System;
using Ovomium.Core.Update;

var v = args.Length == 1 ? InstalledVersion.FromMelonDll(args[0]) : null;
if (v == null)
{
    Console.Error.WriteLine($"MelonInfo « {InstalledVersion.MelonName} » illisible : {(args.Length == 1 ? args[0] : "usage : MelonVersion <dll>")}");
    return 1;
}
Console.WriteLine(v);
return 0;
