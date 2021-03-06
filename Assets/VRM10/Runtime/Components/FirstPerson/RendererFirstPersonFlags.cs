using System;
using UnityEngine;

namespace UniVRM10
{
    [Serializable]
    public struct RendererFirstPersonFlags
    {
        public Renderer Renderer;
        public VrmLib.FirstPersonMeshType FirstPersonFlag;
        public Mesh SharedMesh
        {
            get
            {
                var renderer = Renderer as SkinnedMeshRenderer;
                if (renderer != null)
                {
                    return renderer.sharedMesh;
                }

                var filter = Renderer.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    return filter.sharedMesh;
                }

                return null;
            }
        }
    }
}
