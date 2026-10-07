;; p_plotcfg.lsp -- dump plot configuration of a DWG (ASCII only, no Chinese).
;; Purpose: locate why PISCES plots come out LIGHT (lines thin/pale, text still readable).
;;
;; Contract with the caller (.scr):
;;   (setq PT_FH (open "...out_plotcfg.txt" "w"))   ; opened by the script
;;   (load "...p_plotcfg.lsp")
;;   (setq PT_R (vl-catch-all-apply 'p_all nil))    ; any LISP error is captured, not fatal
;;   then write PT_R error into the file and close.
;;
;; NOTE: every getvar/ssget goes through _g / _sx so that one unsupported variable
;;       cannot abort the whole dump (accoreconsole has its own set of valid vars).

(defun _o (s) (write-line s PT_FH))

(defun _g (name / r)
  (setq r (vl-catch-all-apply 'getvar (list name)))
  (cond ((vl-catch-all-error-p r) (strcat "<ERR " (vl-catch-all-error-message r) ">"))
        ((null r) "<nil>")
        ((= (type r) 'STR) r)
        ((= (type r) 'REAL) (rtos r 2 8))
        (T (itoa (atoi (rtos r 2 0))))))   ;; AutoLISP (fix 0.0) errors -> go through rtos/atoi

(defun _v2s (v / d)
  (cond ((null v) "")
        ((= (type (cdr v)) 'STR) (cdr v))
        ((= (type (cdr v)) 'REAL) (rtos (cdr v) 2 6))
        ((= (type (cdr v)) 'INT) (itoa (cdr v)))
        (T (vl-princ-to-string (cdr v)))))

(defun _bits (val / i b out)
  (if (not (numberp val))
    (strcat "<non-numeric: " (vl-princ-to-string val) ">")
    (progn
      ;; AutoLISP (fix 0.0) errors with "fixnump: 0.0" -> normalize REAL to INT via rtos/atoi.
      (setq val (atoi (rtos val 2 0)))
      (setq out "" i 0)
      (while (< i 24)
        (setq b (fix (/ val (expt 2 i))))
        (if (= b 2)
          (setq out (strcat out " +" (itoa (expt 2 i))))
          (setq b (- b (* 2 b))))
        (setq i (+ i 1)))
      (strcat (itoa val) " =>" out))))

(defun _layobj (nm / d lst r)
  (setq r (vl-catch-all-apply 'dictsearch (list (namedobjdict) "ACAD_LAYOUT")))
  (if (vl-catch-all-error-p r)
    nil
    (progn
      (setq d r)
      (setq lst (member (cons 3 nm) d))
      (if lst (cdr (assoc 350 lst)) nil))))

(defun p_layout (nm / lay e v)
  (setq lay (_layobj nm))
  (_o (strcat "== LAYOUT [" nm "]"))
  (if (null lay)
    (_o "   NOT FOUND")
    (progn
      (setq e (entget lay))
      (foreach v e
        (if (member (car v) '(100 330))
          nil
          (_o (strcat "   g" (itoa (car v)) " = " (_v2s v)))))
      (setq v (assoc 70 e))
      (if v (_o (strcat "   >> [70] " (_bits (cdr v)))))
      (setq v (assoc 146 e))
      (if v (_o (strcat "   >> [146 shade-plot] " (_bits (cdr v)))))
      (setq v (assoc 147 e))
      (if v (_o (strcat "   >> [147] " (_bits (cdr v)))))
      (terpri))))

(defun p_vars (/ v)
  (foreach v '("SHADEPLOT" "LWDISPLAY" "LWDSCALE" "PSVPSCALE" "PSTYLEPOLICY" "COLOR"
               "TILEMODE" "CTAB" "STYLESHEET" "FILLMODE" "CMLSTYLE" "LTSCALE" "CELTSCALE"
               "PSLTSCALE" "MSLTSCALE" "SPLINETYPE" "PDMODE" "PDSIZE" "HPMAXLINES"
               "HPMAXLAYERS" "HATCHGENERATEBOUNDARY" "DIMSCALE" "ANGBASE" "MEASUREMENT"
               "INSUNITS" "PLINEGEN" "SKETCHINC" "SPLINESEGS" "SURFU" "ISOLINES"
               "CECOLOR" "CELTYPE" "CELWEIGHT" "ACADVER")
    (_o (strcat "SYSVAR " v " = " (_g v)))))

(defun _sx (filter)
  (let ((r (vl-catch-all-apply 'ssget (list "_X" filter))))
    (if (vl-catch-all-error-p r) nil r)))

(defun p_types (/ ty ss i tot)
  ;; NOTE: never name a local "t" here - it shadows the AutoLISP constant T and
  ;; turns every bare T inside the function into a variable reference.
  (setq tot 0)
  (foreach ty '("LINE" "LWPOLYLINE" "POLYLINE" "ARC" "CIRCLE" "SPLINE" "ELLIPSE"
                "TEXT" "MTEXT" "ATTDEF" "ATTRIB" "INSERT" "DIMENSION" "LEADER" "POINT"
                "HATCH" "SOLID" "3DFACE" "IMAGE" "OLE2FRAME" "WIPEOUT" "3DSOLID"
                "REGION" "BODY" "MESH" "SURFACE" "SHAPE" "RAY" "XLINE" "MLINE")
    (setq ss (_sx (list (cons 0 ty))))
    (if (null ss)
      nil
      (progn
        (setq i (sslength ss))
        (_o (strcat "COUNT " ty " = " (itoa i)))
        (setq tot (+ tot i)))))
  (_o (strcat "TOTAL (listed types) = " (itoa tot))))

(defun _bump (key alist / hit)
  (if (setq hit (assoc key alist))
    (subst (cons key (1+ (cdr hit))) hit alist)
    (append alist (list (cons key 1)))))

(defun _hist (ss code / i e v key lst)
  (setq lst nil)
  (if ss
    (progn
      (setq i 0)
      (while (< i (sslength ss))
        (setq e (entget (ssname ss i)))
        (setq v (assoc code e))
        (setq key (if v (_v2s v) "<none>"))
        (setq lst (_bump key lst))
        (setq i (+ i 1)))))
  (if (null lst)
    (_o "   <none>")
    (foreach key lst (_o (strcat "   g" (itoa code) " " (car key) " x" (itoa (cdr key)))))))

(defun p_colors (/ ss)
  (setq ss (_sx '((0 . "LINE,LWPOLYLINE,ARC,CIRCLE,POLYLINE,ELLIPSE,SPLINE,TEXT,MTEXT,HATCH,3DSOLID,REGION,BODY,MESH,INSERT,DIMENSION,SOLID,LEADER"))))
  (_o "== ENTITY COLOR g62 HISTOGRAM  (256=ByLayer 0=ByBlock, negative = off)")
  (_hist ss 62)
  (setq ss (_sx '((0 . "LINE,LWPOLYLINE,ARC,CIRCLE,POLYLINE,ELLIPSE,SPLINE,TEXT,MTEXT,HATCH,3DSOLID,REGION,BODY,MESH,INSERT,DIMENSION,SOLID,LEADER"))))
  (_o "== ENTITY LINEWEIGHT g370 HISTOGRAM (-3=ByLayer -2=ByBlock -1=Default, else mm*100)")
  (_hist ss 370)
  (setq ss (_sx '((0 . "LINE,LWPOLYLINE,ARC,CIRCLE,POLYLINE,ELLIPSE,SPLINE,TEXT,MTEXT,HATCH,3DSOLID,REGION,BODY,MESH,INSERT,DIMENSION,SOLID,LEADER"))))
  (_o "== ENTITY LAYER g8 HISTOGRAM")
  (_hist ss 8))

(defun _rec2p (x)
  ;; true when x is a symbol-table record, i.e. ((2 . "name") (70 . 0) ...)
  ;; NOTE: AutoLISP has no PAIRP (measured 2026-10-07) -> use listp + car-of-car test.
  (and (listp x) (listp (car x)) (= (car (car x)) 2)))

(defun _layerrecords (/ r out rec)
  ;; (tblnext "LAYER" T) may return ONE record or the WHOLE table as a list of records
  ;; (measured: it returns the whole table here). Normalize to a list of records.
  (setq out nil)
  (setq r (vl-catch-all-apply 'tblnext (list "LAYER" T)))
  (if (not (vl-catch-all-error-p r))
    (progn
      (if (_rec2p r)
        (setq out (list r))
        (foreach rec r
          (if (_rec2p rec) (setq out (append out (list rec))))))))
  out)

(defun p_layers (/ recs rec)
  (_o "== LAYER TABLE (name | color g62 | lineweight g370 | flags g70)")
  (setq recs (_layerrecords))
  (if (null recs)
    (_o "   <LAYER table not readable via tblnext in accoreconsole - see the g8/g62/g370 entity histograms below>")
    (foreach rec recs
      (_o (strcat "   " (_v2s (assoc 2 rec)) " | color=" (_v2s (assoc 62 rec))
           " | lw=" (_v2s (assoc 370 rec)) " | flags=" (_v2s (assoc 70 rec)))))))

(defun p_layerstats (/ ss i e nm lst r)
  ;; Fallback layer census built from entities: for every distinct layer name seen on
  ;; entities, look the LAYER table record up via tblsearch.
  (setq ss (_sx '((0 . "LINE,LWPOLYLINE,ARC,CIRCLE,POLYLINE,TEXT,MTEXT,INSERT,DIMENSION,HATCH"))))
  (if (null ss)
    (_o "   <no entities>")
    (progn
      (setq lst nil i 0)
      (while (< i (sslength ss))
        (setq e (entget (ssname ss i)))
        (setq nm (cdr (assoc 8 e)))
        (if (and nm (not (member nm lst))) (setq lst (append lst (list nm))))
        (setq i (+ i 1)))
      (_o (strcat "   distinct layers in use = " (itoa (length lst))))
      (foreach nm lst
        (setq r (vl-catch-all-apply 'tblsearch (list "LAYER" nm)))
        (if (vl-catch-all-error-p r)
          (_o (strcat "   " nm " | <tblsearch ERR>"))
          (_o (strcat "   " nm " | color=" (_v2s (assoc 62 r))
               " | layer-lw=" (_v2s (assoc 370 r)))))))))

(defun _llist (/ d r lst out)
  ;; accoreconsole has NO (layoutlist) -> "bad function: LAYOUTLIST" (measured 2026-10-07).
  ;; Walk the ACAD_LAYOUT dictionary instead: every record after the first is (3 . <name>).
  (setq out nil)
  (setq r (vl-catch-all-apply 'dictsearch (list (namedobjdict) "ACAD_LAYOUT")))
  (if (not (vl-catch-all-error-p r))
    (progn
      (setq d r)
      (foreach lst (cdr d)
        (if (= (car lst) 3) (setq out (cons (cdr lst) out))))))
  (reverse out))

(defun p_all ()
  (terpri)
  (_o "===== DWG PLOT CONFIG DUMP =====")
  (_o (strcat "DWGPREFIX = " (_g "DWGPREFIX")))
  (_o (strcat "CTAB = " (_g "CTAB") "   TILEMODE = " (_g "TILEMODE")))
  (terpri)
  (_o "----- STEP1 LAYOUT LIST -----")
  (foreach l (_llist) (_o (strcat "   " l)))
  (terpri)
  (_o "----- STEP2 SYSVARS -----")
  (p_vars)
  (terpri)
  (_o "----- STEP3 LAYOUT PLOT SETTINGS -----")
  (foreach l (_llist) (p_layout l))
  (terpri)
  (_o "----- STEP4 LAYER TABLE -----")
  (p_layers)
  (terpri)
  (_o "----- STEP5 ENTITY TYPE COUNTS -----")
  (p_types)
  (terpri)
  (_o "----- STEP6 ENTITY COLOR / LINEWEIGHT / LAYER -----")
  (p_colors)
  (terpri)
  (_o "===== END =====")
  (terpri))

(princ)