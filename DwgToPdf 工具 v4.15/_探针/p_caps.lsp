;; p_caps.lsp -- capability probe: which AutoLISP constructs actually work inside
;; accoreconsole.exe? Every check is isolated so one failure cannot hide the rest.
;;
;; Context: while building p_plotcfg.lsp the dump died inside the entity-census step
;; with "no function definition: R" and "bad object to bind: T", which pointed at the
;; ssget wrapper rather than at the drawing. This file isolates each suspect.

(setq PT_N 0)

(defun _t (label r)
  (setq PT_N (1+ PT_N))
  (if (vl-catch-all-error-p r)
    (write-line (strcat (itoa PT_N) ". " label " => ERROR: " (vl-catch-all-error-message r)) PT_FH)
    (write-line (strcat (itoa PT_N) ". " label " => ok: " (vl-princ-to-string r)) PT_FH)))

(defun _sx2 (f)
  (setq r (vl-catch-all-apply 'ssget (list "_X" f)))
  (if (vl-catch-all-error-p r) nil r))

(defun _tc (label thunk / rr)
  (setq PT_N (1+ PT_N))
  (setq rr (vl-catch-all-apply thunk nil))
  (if (vl-catch-all-error-p rr)
    (write-line (strcat (itoa PT_N) ". " label " => ERROR: " (vl-catch-all-error-message rr)) PT_FH)
    (write-line (strcat (itoa PT_N) ". " label " => ok: " (vl-princ-to-string rr)) PT_FH)))

(defun _bump2 (key alist / hit)
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
        (setq key (if v (vl-princ-to-string (cdr v)) "<none>"))
        (setq lst (_bump2 key lst))
        (setq i (+ i 1)))))
  lst)

(defun p_caps ()
  (write-line "===== ACCORECONSOLE LISP CAPABILITY PROBE =====" PT_FH)

  (_t "cons+list" (list (cons 0 "LINE")))
  (_t "length of list" (length (list "a" "b")))
  (_t "sslength of empty ssget" (sslength (ssget "_X" '((0 . "NOSUCHTYPE")))))

  (write-line "---- binding forms ----" PT_FH)
  (_t "let" (let ((q 41)) (+ q 1)))
  (_t "lambda funcall" (funcall (lambda (a b) (+ a b)) 1 2))

  (write-line "---- ssget shapes ----" PT_FH)
  (_t "ssget single type" (ssget "_X" '((0 . "LINE"))))
  (_t "ssget multi type" (ssget "_X" '((0 . "LINE,LWPOLYLINE,ARC,CIRCLE"))))
  (_t "ssget via (list (cons 0 ty))" (ssget "_X" (list (cons 0 "LINE"))))
  (_t "ssget cross-type filter" (ssget "_X" '((0 . "LINE,ARC,TEXT,MTEXT"))))
  (_t "wrapper _sx2 single" (_sx2 (list (cons 0 "LINE"))))
  (_t "wrapper _sx2 multi" (_sx2 (list (cons 0 "LINE,LWPOLYLINE"))))

  (write-line "---- entity access ----" PT_FH)
  (setq PT_S (_sx2 (list (cons 0 "LINE"))))
  (if PT_S
    (progn
      (_t "sslength" (sslength PT_S))
      (_t "entget g62" (cdr (assoc 62 (entget (ssname PT_S 0)))))
      (_t "entget g8 layer" (cdr (assoc 8 (entget (ssname PT_S 0)))))
      (_t "entget g370 lw" (cdr (assoc 370 (entget (ssname PT_S 0)))))
      (write-line "---- histogram ----" PT_FH)
      (_t "hist g62" (_hist PT_S 62))
      (_t "hist g370" (_hist PT_S 370)))
    (write-line "no LINE found" PT_FH))

  (write-line "===== END =====" PT_FH))

;; ------------------------------------------------------------------
;; p_census: entity census reusing the helpers proven to work above.
;; Run with:  run_plotcfg.ps1 -Lsp .\p_caps.lsp -Func p_census
;; ------------------------------------------------------------------
(defun _cn (ss) (if ss (sslength ss) 0))

(defun p_census ()
  (write-line "===== ENTITY CENSUS =====" PT_FH)
  (foreach ty '("LINE" "LWPOLYLINE" "POLYLINE" "ARC" "CIRCLE" "SPLINE" "ELLIPSE"
                "TEXT" "MTEXT" "ATTDEF" "ATTRIB" "INSERT" "DIMENSION" "LEADER" "POINT"
                "HATCH" "SOLID" "3DFACE" "IMAGE" "OLE2FRAME" "WIPEOUT" "3DSOLID"
                "REGION" "BODY" "MESH" "SURFACE" "SHAPE" "RAY" "XLINE" "MLINE")
    (setq ss (_sx2 (list (cons 0 ty))))
    (if (> (_cn ss) 0)
      (write-line (strcat "COUNT " ty " = " (itoa (_cn ss))) PT_FH)))
  (setq ss (_sx2 '((0 . "LINE,LWPOLYLINE,ARC,CIRCLE,POLYLINE,ELLIPSE,SPLINE,TEXT,MTEXT,HATCH,3DSOLID,REGION,BODY,MESH,INSERT,DIMENSION,SOLID,LEADER"))))
  (write-line (strcat "---- MAJOR TYPES TOTAL = " (itoa (_cn ss))) PT_FH)
  (write-line (strcat "COLOR g62 (256=ByLayer 0=ByBlock) => " (vl-princ-to-string (_hist ss 62))) PT_FH)
  (write-line (strcat "LINEWEIGHT g370 (-3=ByLayer -1=Default else mm*100) => " (vl-princ-to-string (_hist ss 370))) PT_FH)
  (write-line (strcat "LAYER g8 => " (vl-princ-to-string (_hist ss 8))) PT_FH)
  (write-line "===== END =====" PT_FH))

(princ)

;; ------------------------------------------------------------------
;; p_let: is `let` usable inside accoreconsole?
;; Both p_plotcfg.lsp and p_census.lsp die with "no function definition: R"
;; on their first statement that calls the ssget wrapper, and the only construct
;; those wrappers use that nothing else uses is `let`.
;; ------------------------------------------------------------------
(defun _te (label expr / rr)
  ;; vl-catch-all-apply needs a SYMBOL, not a lambda object
  ;; ("bad function: #<SUBR -lambda->", measured 2026-10-07) -> feed it (read <string>).
  (setq PT_N (1+ PT_N))
  (setq rr (vl-catch-all-apply 'eval (list (read expr))))
  (if (vl-catch-all-error-p rr)
    (write-line (strcat (itoa PT_N) ". " label " => ERROR: " (vl-catch-all-error-message rr)) PT_FH)
    (write-line (strcat (itoa PT_N) ". " label " => ok: " (vl-princ-to-string rr)) PT_FH)))

(defun p_extents ()
  ;; Plot window shared by every variant of run_plotvars.ps1, so output differences
  ;; are attributable only to the variable under test.
  ;; EXTMIN/EXTMAX may be slightly stale (no ZOOM _E is issued here) but they are
  ;; identical across variants, which is all this probe needs.
  (write-line "===== EXTENTS =====" PT_FH)
  (write-line (strcat "EXTMIN=" (vl-princ-to-string (getvar "EXTMIN"))) PT_FH)
  (write-line (strcat "EXTMAX=" (vl-princ-to-string (getvar "EXTMAX"))) PT_FH)
  (write-line "===== END =====" PT_FH))

(defun p_lwbits ()
  ;; Which DXF group-70 bit is "plot with lineweights"?
  ;; Measured route (2026-10-07): the Layout COM object exposes PlotWithLineweights, NOT
  ;; PlotLineweights. Turn it on via COM, save a throwaway copy, then diff the Model
  ;; layout's plot-settings flags here. That gives the bit to set from the accore session.
  (write-line "===== PLOT FLAGS (Model layout) =====" PT_FH)
  (setq PT_D (dictsearch (namedobjdict) "ACAD_LAYOUT"))
  (setq PT_LAY (cdr (assoc 350 (member (cons 3 "Model") PT_D))))
  (setq PT_LE (entget PT_LAY))
  (foreach PT_C '(2 4 7 40 44 45 48 49 70 72 73 74 140 141 142 143 146 147 148 149)
    (if (assoc PT_C PT_LE)
      (progn
        (setq PT_V (cdr (assoc PT_C PT_LE)))
        (write-line (strcat "g" (itoa PT_C) " = " (vl-princ-to-string PT_V)) PT_FH))))
  (write-line (strcat "LWDISPLAY = " (vl-princ-to-string (getvar "LWDISPLAY"))) PT_FH)
  (write-line (strcat "LWDSCALE  = " (vl-princ-to-string (getvar "LWDSCALE"))) PT_FH)
  (write-line "===== END =====" PT_FH))

(defun _o2 (s) (write-line s PT_FH))

(defun p_let ()
  ;; MEASURED 2026-10-07 on AutoCAD 2026 accoreconsole:
  ;;   (let ((q 41)) (+ q 1))  =>  "no function definition: Q"
  ;; i.e. `let` is NOT available in the Core Console AutoLISP. It is parsed as a
  ;; function call and the interpreter then trips on the first bound symbol - which is
  ;; exactly why the earlier probes reported "no function definition: R" (their ssget
  ;; wrapper was the only code using `let`, and it bound a variable named `r`).
  ;; => write LISP for accoreconsole with setq + defun locals only. NO let.
  (write-line "===== ACCORECONSOLE LISP CONSTRAINT: no `let` =====" PT_FH)
  (write-line "PROOF: (let ((q 41)) (+ q 1)) => no function definition: Q" PT_FH)
  (write-line "USE:   (setq tmp ...) + defun with a / locals list instead" PT_FH)
  (write-line "===== END =====" PT_FH))

(defun p_ssget ()
  ;; ssget shapes that the census relies on (all direct calls, no let, no eval).
  (write-line "===== SSGET SUPPORT PROBE =====" PT_FH)
  (_o2 (strcat "1. single type filter  = " (itoa (sslength (ssget "_X" '((0 . "LINE")))))))
  (_o2 (strcat "2. (list (cons 0 ty))   = " (itoa (sslength (ssget "_X" (list (cons 0 "LINE")))))))
  (_o2 (strcat "3. multi type filter   = " (itoa (sslength (ssget "_X" '((0 . "LINE,ARC,CIRCLE,TEXT")))))))
  (if (null (ssget "_X" '((0 . "NOSUCHTYPE"))))
    (_o2 "4. no-match returns nil = yes (so guard with (null s) before sslength)")
    (_o2 "4. no-match returns nil = NO"))
  (setq PT_S (ssget "_X" '((0 . "LINE,LWPOLYLINE,ARC,CIRCLE,POLYLINE,ELLIPSE,SPLINE,TEXT,MTEXT,HATCH,3DSOLID,REGION,BODY,MESH,INSERT,DIMENSION,SOLID,LEADER"))))
  (if PT_S
    (progn
      (_o2 (strcat "5. major types total  = " (itoa (sslength PT_S))))
      (_o2 (strcat "6. first entget g62   = " (vl-princ-to-string (cdr (assoc 62 (entget (ssname PT_S 0)))))))
      (_o2 (strcat "7. first entget g8    = " (vl-princ-to-string (cdr (assoc 8 (entget (ssname PT_S 0)))))))
      (_o2 (strcat "8. first entget g370  = " (vl-princ-to-string (cdr (assoc 370 (entget (ssname PT_S 0)))))))
      (_o2 (strcat "9. COLOR hist         = " (vl-princ-to-string (_hist PT_S 62))))
      (_o2 (strcat "10. LINEWEIGHT hist   = " (vl-princ-to-string (_hist PT_S 370))))
      (_o2 (strcat "11. LAYER hist        = " (vl-princ-to-string (_hist PT_S 8)))))
    (_o2 "5. major types: no entities matched"))
  (write-line "===== END =====" PT_FH))