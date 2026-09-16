using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public class UmaContainerProp : UmaContainer
{
    /// <summary>
    /// Texture sets of this prop/scene, when any of its materials shipped without a texture
    /// (see <see cref="UmaEnvTextureSet"/>). Null when every material was already textured.
    /// </summary>
    public UmaEnvTextureSet TextureSet { get; private set; }

    public void LoadProp(UmaDatabaseEntry entry, Transform SetParent = null)
    {
        var go = entry.Get<GameObject>();
        var prop = Instantiate(go, SetParent ? SetParent : this.transform);

        // Environment materials often carry no texture at all - the game assigns one of several
        // texture sets at runtime. Without this they render flat white.
        TextureSet = UmaEnvTextureSet.Attach(prop, entry.Name);

        /*
        foreach (Renderer r in prop.GetComponentsInChildren<Renderer>())
        {
            foreach (Material m in r.sharedMaterials)
            {
                //Shaders can be differentiated by checking m.shader.name
                m.shader = Shader.Find("Unlit/Transparent Cutout");
            }
        }
        */
    }
}
