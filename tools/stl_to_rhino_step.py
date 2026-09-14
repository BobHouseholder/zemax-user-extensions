#!/usr/bin/env python3
"""Convert an STL mesh to a Rhino-friendly STEP (AP214IS, mm, few faces).

Proven keep-out recipe (matches Concept-24 ..._coneOnly.step ~1 MANIFOLD / ~27 faces / mm):
  1. StlAPI_Reader -> BRepBuilderAPI_Sewing
  2. Keep-out shell: **largest AABB / Z-span** (ray envelope), not face count
  3. MakeSolid + ShapeFix_Shape + ShapeUpgrade_UnifySameDomain
  4. STEPControl_Controller.Init + Interface_Static:
       write.step.schema=AP214IS, write.step.unit=MM, write.surfacecurve.mode=0
  5. Single Transfer/Write

Multi-solid / named products (--named-solids):
  Parse ASCII STL solids by name (KEEP_OUT, L1, L2, L3, ...), convert each,
  write one STEP assembly with XCAF product names matching the STL solid names.

Usage:
  python tools/stl_to_rhino_step.py in.stl out.step
  python tools/stl_to_rhino_step.py in.stl out.step --named-solids
  Optional: --all-shells  (compound of every shell; legacy)
  Optional: --decimate 0.3
"""
from __future__ import annotations

import argparse
import os
import re
import sys
import tempfile
from collections import Counter


def _count(shape, typ) -> int:
    from OCP.TopExp import TopExp_Explorer

    exp = TopExp_Explorer(shape, typ)
    n = 0
    while exp.More():
        n += 1
        exp.Next()
    return n


def _maybe_decimate(stl_path: str, fraction, tmp_path: str) -> str:
    if not fraction or fraction >= 1.0:
        return stl_path
    import trimesh

    mesh = trimesh.load(stl_path, force="mesh")
    if not isinstance(mesh, trimesh.Trimesh):
        mesh = mesh.dump(concatenate=True)
    target = max(4, int(len(mesh.faces) * float(fraction)))
    simplified = mesh.simplify_quadratic_decimation(target)
    simplified.export(tmp_path)
    print(f"decimated {len(mesh.faces)} -> {len(simplified.faces)} faces -> {tmp_path}")
    return tmp_path


def _shell_to_unified_solid(shell, ang_candidates, lin_candidates):
    from OCP.BRepBuilderAPI import BRepBuilderAPI_MakeSolid
    from OCP.ShapeFix import ShapeFix_Shape
    from OCP.ShapeUpgrade import ShapeUpgrade_UnifySameDomain
    from OCP.TopAbs import TopAbs_FACE

    ms = BRepBuilderAPI_MakeSolid(shell)
    if not ms.IsDone():
        raise RuntimeError("shell is not closed; cannot MakeSolid")
    solid = ms.Solid()
    fixer = ShapeFix_Shape(solid)
    fixer.Perform()
    solid = fixer.Shape()

    best, best_n = solid, _count(solid, TopAbs_FACE)
    for ang in ang_candidates:
        for lin in lin_candidates:
            uni = ShapeUpgrade_UnifySameDomain(solid, True, True, True)
            uni.SetLinearTolerance(float(lin))
            uni.SetAngularTolerance(float(ang))
            uni.Build()
            sh = uni.Shape()
            n = _count(sh, TopAbs_FACE)
            if n < best_n:
                best, best_n = sh, n
    fixer = ShapeFix_Shape(best)
    fixer.Perform()
    return fixer.Shape(), best_n


def _sew_shells(stl_path: str):
    from OCP.StlAPI import StlAPI_Reader
    from OCP.TopoDS import TopoDS_Shape, TopoDS
    from OCP.BRepBuilderAPI import BRepBuilderAPI_Sewing
    from OCP.TopAbs import TopAbs_FACE, TopAbs_SHELL
    from OCP.TopExp import TopExp_Explorer
    from OCP.Bnd import Bnd_Box
    from OCP.BRepBndLib import BRepBndLib

    reader = StlAPI_Reader()
    shape = TopoDS_Shape()
    if not reader.Read(shape, stl_path):
        raise RuntimeError(f"StlAPI_Reader failed: {stl_path}")

    sewer = BRepBuilderAPI_Sewing(1e-6)
    sewer.Add(shape)
    sewer.Perform()
    sewn = sewer.SewedShape()

    shells = []
    exp = TopExp_Explorer(sewn, TopAbs_SHELL)
    while exp.More():
        sh = TopoDS.Shell(exp.Current())
        nf = _count(sh, TopAbs_FACE)
        box = Bnd_Box()
        BRepBndLib.Add_s(sh, box, True)
        xmin, ymin, zmin, xmax, ymax, zmax = box.Get()
        dx, dy, dz = xmax - xmin, ymax - ymin, zmax - zmin
        vol = max(dx, 0.0) * max(dy, 0.0) * max(dz, 0.0)
        zspan = abs(dz)
        score = (zspan, vol, -nf)
        shells.append((score, nf, zspan, vol, sh))
        exp.Next()
    if not shells:
        raise RuntimeError("no shells after sewing")
    shells.sort(key=lambda t: t[0], reverse=True)
    for i, (score, nf, zspan, vol, _) in enumerate(shells):
        print(f"shell[{i}] faces_raw={nf} zspan={zspan:.6g} aabb_vol={vol:.6g}")
    return shells


def _split_ascii_stl(stl_path: str):
    """Yield (name, path) for each solid in an ASCII STL. Binary -> one ('SOLID', path)."""
    with open(stl_path, "rb") as f:
        head = f.read(200)
    if not head.lstrip().lower().startswith(b"solid"):
        yield "SOLID", stl_path
        return
    # Heuristic: if 'endsolid' appears, treat as ASCII multi-solid capable
    text = open(stl_path, "r", errors="ignore").read()
    if "endsolid" not in text.lower():
        yield "SOLID", stl_path
        return
    parts = []
    cur_name = None
    cur_lines = []
    for line in text.splitlines(True):
        ls = line.lstrip().lower()
        if ls.startswith("solid"):
            if cur_name is not None and cur_lines:
                parts.append((cur_name, "".join(cur_lines)))
            rest = line.strip()[5:].strip()
            cur_name = rest if rest else f"SOLID{len(parts)+1}"
            cur_name = re.sub(r"[^A-Za-z0-9_]+", "_", cur_name)[:40] or f"SOLID{len(parts)+1}"
            cur_lines = [line]
        elif ls.startswith("endsolid"):
            cur_lines.append(line)
            if cur_name is not None:
                parts.append((cur_name, "".join(cur_lines)))
            cur_name, cur_lines = None, []
        else:
            if cur_name is not None:
                cur_lines.append(line)
    if cur_name is not None and cur_lines:
        parts.append((cur_name, "".join(cur_lines)))
    if not parts:
        yield "SOLID", stl_path
        return
    tmpdir = tempfile.mkdtemp(prefix="stl_named_")
    for i, (name, body) in enumerate(parts):
        path = os.path.join(tmpdir, f"{i:02d}_{name}.stl")
        with open(path, "w", encoding="ascii", errors="ignore") as f:
            f.write(body)
        yield name, path


def _write_step_shapes(named_shapes, out_path: str, schema: str, assembly: int):
    """Write one or more named shapes to STEP with product names via XCAF when possible."""
    from OCP.STEPControl import STEPControl_Writer, STEPControl_AsIs, STEPControl_Controller
    from OCP.Interface import Interface_Static
    from OCP.IFSelect import IFSelect_ReturnStatus
    from OCP.BRep import BRep_Builder
    from OCP.TopoDS import TopoDS_Compound

    STEPControl_Controller.Init_s()
    Interface_Static.SetCVal_s("write.step.schema", schema)
    Interface_Static.SetCVal_s("write.step.unit", "MM")
    Interface_Static.SetIVal_s("write.surfacecurve.mode", 0)
    Interface_Static.SetIVal_s("write.step.assembly", int(assembly))

    # Prefer XCAF for named products (KEEP_OUT, L1, ...)
    try:
        from OCP.TDocStd import TDocStd_Document
        from OCP.XCAFApp import XCAFApp_Application
        from OCP.XCAFDoc import XCAFDoc_DocumentTool
        from OCP.STEPCAFControl import STEPCAFControl_Writer
        from OCP.TCollection import TCollection_ExtendedString, TCollection_HAsciiString
        from OCP.TDataStd import TDataStd_Name

        app = XCAFApp_Application.GetApplication_s()
        doc = TDocStd_Document(TCollection_ExtendedString("MDTV-XCAF"))
        app.InitDocument(doc)
        shape_tool = XCAFDoc_DocumentTool.ShapeTool_s(doc.Main())
        for name, shape in named_shapes:
            label = shape_tool.AddShape(shape, False, False)
            TDataStd_Name.Set_s(label, TCollection_ExtendedString(name))
            # Also set the product name used by some STEP readers
            try:
                from OCP.XCAFDoc import XCAFDoc_ShapeTool
                # best-effort; naming via TDataStd_Name is usually enough for Rhino
            except Exception:
                pass
            print(f"XCAF product: {name}")
        writer = STEPCAFControl_Writer()
        writer.SetNameMode(True)
        if not writer.Transfer(doc, STEPControl_AsIs):
            raise RuntimeError("STEPCAF Transfer failed")
        if writer.Write(out_path) != IFSelect_ReturnStatus.IFSelect_RetDone:
            raise RuntimeError("STEPCAF Write failed")
        return
    except Exception as ex:
        print(f"XCAF named write unavailable ({ex}); falling back to STEPControl_Writer")

    writer = STEPControl_Writer()
    if len(named_shapes) == 1:
        to_xfer = named_shapes[0][1]
    else:
        builder = BRep_Builder()
        comp = TopoDS_Compound()
        builder.MakeCompound(comp)
        for _, s in named_shapes:
            builder.Add(comp, s)
        to_xfer = comp
    if writer.Transfer(to_xfer, STEPControl_AsIs) != IFSelect_ReturnStatus.IFSelect_RetDone:
        raise RuntimeError("STEP Transfer failed")
    if writer.Write(out_path) != IFSelect_ReturnStatus.IFSelect_RetDone:
        raise RuntimeError("STEP Write failed")


def stl_to_rhino_step(
    stl_path: str,
    out_path: str,
    *,
    ang_candidates=None,
    lin_candidates=None,
    assembly: int = 1,
    schema: str = "AP214IS",
    decimate=None,
    all_shells: bool = False,
    named_solids: bool = False,
) -> dict:
    from OCP.TopAbs import TopAbs_FACE

    if ang_candidates is None:
        ang_candidates = (1e-3, 5e-3, 1e-2, 0.02, 0.05, 0.1, 0.15, 0.2, 0.3)
    if lin_candidates is None:
        lin_candidates = (1e-4, 1e-3, 1e-2)

    tmp = out_path + ".decim.stl"
    src = _maybe_decimate(stl_path, decimate, tmp)

    named_shapes = []
    face_counts = []
    shells_in = 0

    if named_solids:
        for name, part_path in _split_ascii_stl(src):
            shells = _sew_shells(part_path)
            shells_in += len(shells)
            # Per named solid: if KEEP_OUT and multiple shells, prefer largest Z-span;
            # otherwise use all shells of that STL solid (usually 1).
            if name.upper().startswith("KEEP_OUT") and not all_shells:
                chosen = [shells[0][4]]
                _score, nf, zspan, vol, _ = shells[0]
                print(
                    f"{name}: KEEP-OUT shell zspan={zspan:.6g} aabb_vol={vol:.6g} faces_raw={nf}"
                )
            else:
                chosen = [sh for *_, sh in shells]
                print(f"{name}: using {len(chosen)} shell(s)")
            # One solid per named product: sew chosen into one if needed
            from OCP.BRep import BRep_Builder
            from OCP.TopoDS import TopoDS_Compound
            if len(chosen) == 1:
                solid, nfaces = _shell_to_unified_solid(chosen[0], ang_candidates, lin_candidates)
            else:
                # unify each then compound under one product
                solids = []
                nfaces = 0
                for shell in chosen:
                    s, nf = _shell_to_unified_solid(shell, ang_candidates, lin_candidates)
                    solids.append(s)
                    nfaces += nf
                builder = BRep_Builder()
                comp = TopoDS_Compound()
                builder.MakeCompound(comp)
                for s in solids:
                    builder.Add(comp, s)
                solid = comp
            named_shapes.append((name, solid))
            face_counts.append(nfaces)
            print(f"{name}: faces after unify={nfaces}")
    else:
        shells = _sew_shells(src)
        shells_in = len(shells)
        if all_shells:
            chosen = [sh for *_, sh in shells]
            print(f"using ALL {len(chosen)} shells")
        else:
            chosen = [shells[0][4]]
            _score, nf, zspan, vol, _ = shells[0]
            print(
                f"using KEEP-OUT shell (zspan={zspan:.6g}, aabb_vol={vol:.6g}, faces_raw={nf}; "
                f"dropped {len(shells)-1} smaller)"
            )
        for i, shell in enumerate(chosen):
            solid, nfaces = _shell_to_unified_solid(shell, ang_candidates, lin_candidates)
            named_shapes.append((f"SOLID{i+1}" if len(chosen) > 1 else "KEEP_OUT", solid))
            face_counts.append(nfaces)
            print(f"solid faces after unify: {nfaces}")

    _write_step_shapes(named_shapes, out_path, schema, assembly)

    if os.path.isfile(tmp):
        try:
            os.remove(tmp)
        except OSError:
            pass

    info = validate_step(out_path)
    info["face_counts"] = face_counts
    info["shells_in"] = shells_in
    info["shells_out"] = len(named_shapes)
    info["products"] = [n for n, _ in named_shapes]
    return info


def validate_step(path: str) -> dict:
    text_head = open(path, "r", errors="ignore").read(12000)
    has_end = False
    c = Counter()
    products = []
    with open(path, "r", errors="ignore") as f:
        for line in f:
            if line.startswith("END-ISO"):
                has_end = True
            m = re.match(r"#\d+\s*=\s*([A-Z0-9_]+)", line)
            if m:
                c[m.group(1)] += 1
            pm = re.search(r"PRODUCT\('([^']+)'", line)
            if pm:
                products.append(pm.group(1))
    has_mm = "SI_UNIT(.MILLI.,.METRE.)" in text_head
    if not has_mm:
        with open(path, errors="ignore") as f:
            for line in f:
                if "MILLI" in line and "METRE" in line:
                    has_mm = True
                    break
    info = {
        "path": path,
        "size": os.path.getsize(path),
        "END-ISO": has_end,
        "LENGTH_UNIT_mm": has_mm,
        "MANIFOLD_SOLID_BREP": c["MANIFOLD_SOLID_BREP"],
        "ADVANCED_FACE": c["ADVANCED_FACE"],
        "PCURVE": c["PCURVE"],
        "FACETED_BREP": c["FACETED_BREP"],
        "PRODUCT_names": products,
    }
    return info


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("stl")
    p.add_argument("step")
    p.add_argument("--decimate", type=float, default=None, help="keep face fraction via trimesh")
    p.add_argument("--schema", default="AP214IS", help="AP214IS or AP203")
    p.add_argument("--assembly", type=int, default=1)
    p.add_argument(
        "--all-shells",
        action="store_true",
        help="emit every sewn shell (legacy). Default keeps largest shell only (keep-out).",
    )
    p.add_argument(
        "--named-solids",
        action="store_true",
        help="split ASCII STL solids and emit named STEP products (KEEP_OUT, L1, ...).",
    )
    args = p.parse_args(argv)
    info = stl_to_rhino_step(
        args.stl,
        args.step,
        schema=args.schema,
        assembly=args.assembly,
        decimate=args.decimate,
        all_shells=args.all_shells,
        named_solids=args.named_solids,
    )
    print("OK", info)
    if not info["END-ISO"] or not info["LENGTH_UNIT_mm"] or info["ADVANCED_FACE"] < 1:
        return 2
    if not args.named_solids and not args.all_shells and info["MANIFOLD_SOLID_BREP"] != 1:
        print("WARN: expected 1 MANIFOLD for keep-out", info["MANIFOLD_SOLID_BREP"], file=sys.stderr)
        return 4
    if args.named_solids and info["MANIFOLD_SOLID_BREP"] < 1:
        print("WARN: expected >=1 MANIFOLD for named solids", info["MANIFOLD_SOLID_BREP"], file=sys.stderr)
        return 4
    return 0


if __name__ == "__main__":
    sys.exit(main())
