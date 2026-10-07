;; p_types.lsp -- per-type entity census.
;;
;; WHY (measured 2026-10-07): the tool's PDF output is VECTOR for the whole H56E corpus
;; at every sheet size (A2..4A0, 0 raster tiles apart from a few embedded images), but the
;; 8 PISCES 2A0 sheets come out as 120 full-page raster tiles. Same sheet size, so the
;; trigger must be something IN the drawing. Prime suspect: 3D/region entities
;; (3DSOLID/REGION/BODY/MESH/SURFACE/3DFACE), which a PDF driver cannot express as
;; vectors and therefore rasterizes that area.
;;
;; Writes its own output file so a failure cannot be swallowed by the caller's buffering.
(defun p_types (/ PT_H PT_S PT_N PT_T)
  (setq PT_H (open "C:/Temp/dwgtopdf_probe/types.txt" "w"))
  (write-line "TYPE CENSUS (0 = not present)" PT_H)
  (foreach PT_T '("3DSOLID" "REGION" "BODY" "MESH" "SURFACE" "3DFACE" "REGION2D"
                  "IMAGE" "OLE2FRAME" "WIPEOUT" "HATCH" "SOLID" "TEXT" "MTEXT"
                  "LEADER" "DIMENSION" "INSERT" "LWPOLYLINE" "POLYLINE" "SPLINE"
                  "ELLIPSE" "ARC" "CIRCLE" "LINE" "XLINE" "RAY" "POINT" "VIEWPORT")
    (setq PT_S (vl-catch-all-apply 'ssget (list "_X" (list (cons 0 PT_T)))))
    (if (vl-catch-all-error-p PT_S)
      (write-line (strcat PT_T " -> ERROR") PT_H)
      (write-line (strcat PT_T " -> " (itoa (cond ((null PT_S) 0) (T (sslength PT_S))))) PT_H)))
  (close PT_H))
