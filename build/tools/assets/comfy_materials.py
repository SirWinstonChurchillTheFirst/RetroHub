"""Drive a running ComfyUI (HTTP API) to render the material scans of materials.json with FLUX.2 klein base 4B.

Usage: python comfy_materials.py <outdir> [--host 127.0.0.1:8189] [--only name1,name2]
ComfyUI must already run (started hidden by run_comfy.ps1, see README.md); this script only talks to /prompt, /history and /view.
Each material is rendered once per seed into <outdir>/raw/<name>-<seed>.png.
"""
import json
import os
import sys
import time
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))


def graph(prompt, negative, seed, size, steps, cfg):
    return {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": "flux-2-klein-base-4b-fp8.safetensors", "weight_dtype": "default"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen_3_4b.safetensors", "type": "flux2", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "flux2-vae.safetensors"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["2", 0]}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"text": negative, "clip": ["2", 0]}},
        "6": {"class_type": "CFGGuider", "inputs": {"model": ["1", 0], "positive": ["4", 0], "negative": ["5", 0], "cfg": cfg}},
        "7": {"class_type": "RandomNoise", "inputs": {"noise_seed": seed}},
        "8": {"class_type": "KSamplerSelect", "inputs": {"sampler_name": "euler"}},
        "9": {"class_type": "Flux2Scheduler", "inputs": {"steps": steps, "width": size, "height": size}},
        "10": {"class_type": "EmptyFlux2LatentImage", "inputs": {"width": size, "height": size, "batch_size": 1}},
        "11": {"class_type": "SamplerCustomAdvanced", "inputs": {"noise": ["7", 0], "guider": ["6", 0], "sampler": ["8", 0],
                                                                "sigmas": ["9", 0], "latent_image": ["10", 0]}},
        "12": {"class_type": "VAEDecode", "inputs": {"samples": ["11", 0], "vae": ["3", 0]}},
        "13": {"class_type": "SaveImage", "inputs": {"images": ["12", 0], "filename_prefix": "rh-mat"}},
    }


def call(host, path, data=None):
    req = urllib.request.Request(f"http://{host}{path}", data=json.dumps(data).encode() if data is not None else None,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def main():
    out = sys.argv[1]
    host = "127.0.0.1:8189"
    only = None
    args = sys.argv[2:]
    for i, a in enumerate(args):
        if a == "--host":
            host = args[i + 1]
        if a == "--only":
            only = set(args[i + 1].split(","))
    cfgfile = json.load(open(os.path.join(HERE, "materials.json"), encoding="utf-8"))
    os.makedirs(os.path.join(out, "raw"), exist_ok=True)
    for m in cfgfile["materials"]:
        if only and m["name"] not in only:
            continue
        for seed in m["seeds"]:
            dest = os.path.join(out, "raw", f"{m['name']}-{seed}.png")
            if os.path.exists(dest):
                print("skip", dest)
                continue
            g = graph(m["prompt"], cfgfile["negative"], seed, m.get("size", cfgfile["size"]), cfgfile["steps"], cfgfile["cfg"])
            pid = json.loads(call(host, "/prompt", {"prompt": g, "client_id": "rh-assets"}))["prompt_id"]
            t0 = time.time()
            while True:
                time.sleep(1.5)
                hist = json.loads(call(host, f"/history/{pid}"))
                if pid in hist:
                    h = hist[pid]
                    if h.get("status", {}).get("status_str") == "error":
                        raise SystemExit(f"comfy error: {h['status']}")
                    imgs = [i for o in h["outputs"].values() for i in o.get("images", [])]
                    if imgs:
                        break
                if time.time() - t0 > 240:
                    raise SystemExit("timeout")
            im = imgs[0]
            q = urllib.parse.urlencode({"filename": im["filename"], "subfolder": im["subfolder"], "type": im["type"]})
            with open(dest, "wb") as f:
                f.write(call(host, f"/view?{q}"))
            print(f"{dest}  {time.time() - t0:.1f}s", flush=True)


if __name__ == "__main__":
    main()
