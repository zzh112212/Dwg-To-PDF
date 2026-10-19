; extframe_company v1 - 公司版：仅对"已知图框层"集合内的 LWPOLYLINE 求盒，快速。
; WARNING: Save as ANSI/GB2312, NOT UTF-8.
(defun cmin (a b) (if (< a b) a b))
(defun cmax (a b) (if (> a b) a b))
(defun pts->box (pts / box)
  (setq box nil)
  (foreach p pts
    (if (and p (= (length p) 2))
      (if box
        (setq box (list (cmin (car box) (car p)) (cmin (cadr box) (cadr p))
                        (cmax (caddr box) (car p)) (cmax (cadddr box) (cadr p))))
        (setq box (list (car p) (cadr p) (car p) (cadr p))))))
  box)
(defun get-pts (d / r g)
  (setq r nil)
  (foreach g d
    (if (and (>= (car g) 10) (<= (car g) 14))
      (setq r (cons (cdr g) r))))
  r)
(defun extframe-company (outfile / fp ss i n e d lay box best imgn imge imgbad id de pp si k kn p lo le g76)
  (setvar "CMDECHO" 0)
  (setq fp (open outfile "w"))
  (setq ss (ssget "_X" (list (cons 0 "LWPOLYLINE"))))
  (setq n 0 i 0 best nil)
  (if ss (setq n (sslength ss)))
  (while (and ss (< i n))
    (setq e (ssname ss i))
    (setq d (entget e))
    (setq lay (cdr (assoc 8 d)))
    ; 已知图框层：图层图框 / 图框图层（并集，GB2312 写入）
    (if (or (equal lay "图层图框") (equal lay "图框图层"))
      (progn
        (setq box (pts->box (get-pts d)))
        (if box
          (setq best (if best
                       (list (cmin (car best) (car box)) (cmin (cadr best) (cadr box))
                             (cmax (caddr best) (caddr box)) (cmax (cadddr best) (cadddr box)))
                       box)))))
    (setq i (1+ i)))
  (if best
    (princ (strcat (rtos (car best) 2 6) " " (rtos (cadr best) 2 6) " "
                   (rtos (caddr best) 2 6) " " (rtos (nth 3 best) 2 6)
                   "|" lay "|company\n") fp)
    (princ "-1|ERR|no-company-frame\n" fp))
  ; v4.15：含图片图纸检测 —— 供出图阶段改走 COM 高清出图
  ; IMAGE（光栅图像）+ 图片型 OLE2FRAME（组码3含 Picture）+ *UNDERLAY（参考底图）
      (setq imgn 0 imge 0 imgbad 0)
    (if (setq si (ssget "_X" (list (cons 410 "Model") (cons 0 "IMAGE"))))
      (progn
        (setq imgn (sslength si) imge (sslength si) k 0 kn (sslength si))
        (while (< k kn)
          (setq d (entget (ssname si k)))
          (setq id (cdr (assoc 340 d)))
          (if id
            (progn
              (setq de (vl-catch-all-apply 'entget (list id)))
              (if (not (vl-catch-all-error-p de))
                (progn
                  (setq pp (cdr (assoc 1 de)))
                  (if (and pp (> (strlen pp) 0) (not (findfile pp))) (setq imgbad (1+ imgbad)))))))
          (setq k (1+ k)))))
  (if (setq si (ssget "_X" (list (cons 410 "Model") (cons 0 "OLE2FRAME"))))
    (progn
      (setq k 0 kn (sslength si))
      (while (< k kn)
        (setq d (entget (ssname si k)) p (cdr (assoc 3 d)))
        (if (and p (wcmatch (strcase p) "*PICTURE*")) (setq imgn (1+ imgn)))
        (setq k (1+ k)))))
  (if (setq si (ssget "_X" (list (cons 410 "Model") (cons 0 "PDFUNDERLAY,DWFUNDERLAY,DGNUNDERLAY"))))
    (setq imgn (+ imgn (sslength si))))
  (if fp (princ (strcat "IMG|" (itoa imgn) "\n") fp))
    (if fp (princ (strcat "IMGR|" (itoa imge) "|" (itoa imgbad) "\n") fp))
  ; v4.16 Model layout shade-plot (DXF group 76). Missing/nil -> 0. Never errors.
  (setq g76 (vl-catch-all-apply '(lambda ()
    (progn
      (setq lo (cdr (assoc 350 (member (cons 3 "Model") (dictsearch (namedobjdict) "ACAD_LAYOUT")))))
      (if lo
        (progn
          (setq le (entget lo))
          (if (assoc 76 le) (cdr (assoc 76 le)) 0))
        0)))))
  (if (or (vl-catch-all-error-p g76) (null g76) (/= (type g76) 'INT)) (setq g76 0))
  (if fp (princ (strcat "G76|" (itoa g76) "\n") fp))
  (if fp (close fp))
  (princ))
