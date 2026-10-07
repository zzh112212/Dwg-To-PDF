;; p_shade.lsp -- read / write plot-settings groups on the Model layout record.
;;
;; Why a file: AutoCAD's SendCommand input is length-limited (a ~300-char one-liner is
;; silently truncated -> the LISP never runs, no error, no output file). Short inline
;; calls like (load "...")(c:shadeget) stay well under the limit.
;;
;; Layout record = one entity with two sections:
;;   AcDbPlotSettings : 70 flags, 72 units, 73 rotation, 74 plot type, 75 std scale,
;;                      [76 shade plot], 77 resolution level, 78 custom DPI, 147..149
;;   AcDbLayout       : 146 here is ELEVATION (0.0) -- NOT the shade plot.
;; The Plot dialog of an untouched PISCES sheet reads 着色/rendered, which is what makes
;; the whole page raster. 76 is the field to move; this file lets us read and set it.

(defun _shade-lay ( / r )
  (setq r (dictsearch (namedobjdict) "ACAD_LAYOUT"))
  (cdr (assoc 350 (member (cons 3 "Model") r))))

(defun _shade-dump ( path / le fp )
  (setq le (entget (_shade-lay)))
  (setq fp (open path "w"))
  (if (null fp)
    (princ "\n*** cannot open output file ***")
    (progn (prin1 le fp) (close fp)))
  (princ))

(defun c:shadeget  ( ) (_shade-dump "C:/Temp/dwgtopdf_gui/shade_before.txt"))
(defun c:shadeget2 ( ) (_shade-dump "C:/Temp/dwgtopdf_gui/shade_after.txt"))

;; (c:setgrp 76 0)  -> set DXF group 76 to 0 on the Model layout record
(defun c:setgrp ( g v / lay le old )
  (setq lay (_shade-lay))
  (setq le  (entget lay))
  (setq old (assoc g le))
  (if old
    (setq le (subst (cons g v) old le))
    (setq le (append le (list (cons g v)))))
  (entmod le)
  (princ))

