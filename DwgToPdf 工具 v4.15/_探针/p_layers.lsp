;; p_layers.lsp -- layer-table lineweight probe (standalone: loading this file must not
;; depend on any other probe file, and it opens its OWN output handle).
;; Question: the PISCES 2A0 sheets allegedly need "plot lineweights" while other drawings
;; do not. Measured elsewhere: PISCES has 87.2% entities with NO group 370 at all (ByLayer)
;; and 15950/16043 entities on layer "0", so layer 0's own lineweight decides what
;; "enable lineweights" actually renders for them.
(defun p_layers ()
  (setq PT_H (open "C:/Temp/dwgtopdf_probe/layers.txt" "w"))
  (write-line "START" PT_H)
  (setq PT_D (vl-catch-all-apply 'tblobjname (list "LAYER" "0")))
  ;; MEASURED 2026-10-07: (tblobjname "LAYER" "0") WORKS in accoreconsole and returns an
  ;; ename -- but an ename is NOT a list, so (assoc 2 ename) fails with
  ;; "bad argument type: listp <entity name>". It must be wrapped in (entget ...).
  ;; Walking the table with entnext from layer "0" covers every layer record.
  (if (vl-catch-all-error-p PT_D)
    (write-line (strcat "tblobjname failed: " (vl-catch-all-error-message PT_D)) PT_H)
    (progn
      (setq PT_N 0)
      (while (and PT_D (< PT_N 60))
        (setq PT_N (1+ PT_N))
        (setq PT_R (vl-catch-all-apply 'entget (list PT_D)))
        (if (vl-catch-all-error-p PT_R)
          (write-line (strcat "  entget failed at #" (itoa PT_N) ": " (vl-catch-all-error-message PT_R)) PT_H)
          (write-line (strcat "layer=" (vl-princ-to-string (cdr (assoc 2 PT_R)))
                              "  lw370=" (vl-princ-to-string (cdr (assoc 370 PT_R)))
                              "  color62=" (vl-princ-to-string (cdr (assoc 62 PT_R)))) PT_H))
        (setq PT_D (vl-catch-all-apply 'entnext (list PT_D))))
      (write-line (strcat "layer count = " (itoa PT_N)) PT_H)))
  (write-line "lw370: -1=ByLayer -2=ByBlock -3=LWDEFAULT(follows display) else absolute 1/100mm" PT_H)
  (write-line "END" PT_H)
  (close PT_H))