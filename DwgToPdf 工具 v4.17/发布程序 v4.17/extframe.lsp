; extframe v2 (DXF) - accoreconsole 兼容版（无 ActiveX，纯 DXF 组码解析）
; v4.14 (A3 识别扫描优化): 单遍化 —— LINE/LWPOLYLINE 的图层盒与扫描共用一次 DXF 解析
;   (旧版 ent-pts 与 v49-scan 各解析一遍)；其余类型不再做无用的 assoc-8/ent-pts；
;   CIRCLE/ARC 不再重复走 10-14 组码（圆心必在半径盒内，gbox 结果不变）。
; WARNING: Save this file as ANSI/GB2312, do NOT convert to UTF-8 (Chinese literals would break).
(defun min2 (a b) (if (< a b) a b))
(defun max2 (a b) (if (> a b) a b))
; ===== v4.9: RECT4 - real rectangular frame detection =====
; Span method: a rectangle exists when 2 vertical LINEs (x1,x2) both span a y-range
; covered by 2 horizontal LINEs (y1,y2) that span [x1,x2] - tolerant to corner overshoot.
; Pool also includes closed axis-aligned LWPOLYLINE rectangles. Largest area wins.
; Then compares full-drawing extents (gbox, zoom-e equivalent) against the rectangle:
; extents within adaptive tolerance -> clean frame (RECT4); extents exceed it ->
; outliers outside frame (RECT4_OUT). Only consulted when legacy/auto candidate would
; fail validation, so normal files keep the old behavior.
(setq v49-tol 2.0)
(setq v49-minshort 100.0)
(defun v49-near (a b tol) (< (abs (- a b)) tol))
(defun v49-area (b) (* (- (caddr b) (car b)) (- (nth 3 b) (cadr b))))
(defun v49-short (b) (min2 (- (caddr b) (car b)) (- (nth 3 b) (cadr b))))
; per-entity collection: gbox extents (all types, point-based), vlines (x ymin ymax),
; hlines (y xmin xmax), prects (closed axis-aligned LWPOLYLINE rectangles)
; note: INSERT contributes its insertion point only (block content not scanned here)
; v4.9.1: collect VERTEX points of a heavy POLYLINE (header group 10 is a dummy (0,0))
; v4.14 (A3): the point-based gbox is NOT accumulated in the hot loop anymore.
; It is only consulted when the zoom-e extent is unusable (degenerate
; EXTMIN/EXTMAX, e.g. every entity on frozen/off layers) - see the fallback
; after the zoom-e block. v49-gbox-rescan reproduces the pre-A3 accumulation
; exactly (same point sets, same add-pt folding), but pays that cost only on
; the rare fallback path instead of for every entity of every file.
(defun v49-gb-poly (ent / sub d p1 pts)
  (setq sub (entnext ent) pts nil)
  (while (and sub (setq d (entget sub)) (/= (cdr (assoc 0 d)) "SEQEND"))
    (setq p1 (cdr (assoc 10 d)))
    (if (and p1 (>= (length p1) 2)) (setq pts (cons p1 pts)))
    (setq sub (entnext sub)))
  pts)
; gbox contributor for one entity - pre-A3 semantics, local gbox via dynscope
(defun v49-gb-scan (d ent / t0 p1 p2 dx dy verts v r)
  (setq t0 (cdr (assoc 0 d)))
  (cond
    ((= t0 "LINE")
     (setq p1 (cdr (assoc 10 d)) p2 (cdr (assoc 11 d)))
     (if (and p1 p2 (>= (length p1) 2) (>= (length p2) 2))
       (progn
         (setq gbox (add-pt gbox (car p1) (cadr p1)))
         (setq gbox (add-pt gbox (car p2) (cadr p2))))))
    ((= t0 "LWPOLYLINE")
     (setq verts (get-pts d))
     (foreach v verts
       (if (and v (>= (length v) 2)) (setq gbox (add-pt gbox (car v) (cadr v))))))
    ((or (= t0 "CIRCLE") (= t0 "ARC"))
     (setq p1 (cdr (assoc 10 d)) r (cdr (assoc 40 d)))
     (if (and p1 r (>= (length p1) 2))
       (progn
         (setq gbox (add-pt gbox (- (car p1) r) (cadr p1)))
         (setq gbox (add-pt gbox (+ (car p1) r) (cadr p1)))
         (setq gbox (add-pt gbox (car p1) (- (cadr p1) r)))
         (setq gbox (add-pt gbox (car p1) (+ (cadr p1) r))))))
    ((or (= t0 "XLINE") (= t0 "HATCH")) nil)
    ((or (= t0 "MTEXT") (= t0 "ACAD_TABLE") (= t0 "RAY"))
     (if (setq p1 (cdr (assoc 10 d)))
       (if (>= (length p1) 2) (setq gbox (add-pt gbox (car p1) (cadr p1))))))
    ((= t0 "POLYLINE")
     (foreach p (v49-gb-poly ent)
       (if (and p (>= (length p) 2)) (setq gbox (add-pt gbox (car p) (cadr p))))))
    ((= t0 "ELLIPSE")
     (if (and (setq p1 (cdr (assoc 10 d))) (setq p2 (cdr (assoc 11 d)))
              (>= (length p1) 2) (>= (length p2) 2))
       (progn
         (setq dx (if (assoc 41 d) (cdr (assoc 41 d)) 1.0))
         (setq gbox (add-pt gbox (+ (car p1) (car p2)) (+ (cadr p1) (cadr p2))))
         (setq gbox (add-pt gbox (- (car p1) (car p2)) (- (cadr p1) (cadr p2))))
         (setq gbox (add-pt gbox (+ (car p1) (* dx (- (cadr p2)))) (+ (cadr p1) (* dx (car p2)))))
         (setq gbox (add-pt gbox (- (car p1) (* dx (- (cadr p2)))) (- (cadr p1) (* dx (car p2))))))))
    (T
     (foreach p (get-pts d)
       (if (and p (>= (length p) 2)) (setq gbox (add-pt gbox (car p) (cadr p))))))))
(defun v49-gbox-rescan (ss / gbox n i ent d)
  (setq gbox nil)
  (setq n (if ss (sslength ss) 0) i 0)
  (while (and ss (< i n))
    (setq ent (ssname ss i) d (entget ent))
    (v49-gb-scan d ent)
    (setq i (1+ i)))
  gbox)
; ===== v4.10: RECT4 upgraded - clustered multi-segment edge coverage =====
; v4.9 required ONE line to span the whole edge -> double-line frames whose outer
; border is broken into several collinear segments were missed (inner frame won and
; the printed PDF cropped the outer frame). New method: cluster near-collinear lines
; (axis distance <= v49-clus-tol), merge their intervals bridging gaps <= v49-gaptol;
; a pair of vertical clusters + horizontal clusters that COVER the x-span (every
; segment inside the span +/- 2mm and total merged length >= v49-cov * span) form
; the rectangle; largest area wins. Single-line frames still work (one interval).
(setq v49-clus-tol 3.0)
(setq v49-gaptol 50.0)
(setq v49-cov 0.9)
; insert e into a list sorted ascending by (car e)
(defun v49-ins (sorted e / out placed)
  (setq out nil placed nil)
  (while sorted
    (if (and (null placed) (< (car e) (car (car sorted))))
      (progn (setq out (cons (car sorted) (cons e out)) placed T))
      (setq out (cons (car sorted) out)))
    (setq sorted (cdr sorted)))
  (if (null placed) (setq out (cons e out)))
  (reverse out))
; ascending sort by (car e)
(defun v49-sort (lst / out)
  (setq out nil)
  (foreach e lst (setq out (v49-ins out e)))
  out)
; merge sorted intervals ((s e)...) bridging gaps <= gap
(defun v49-merge-ivs (ivs gap / out cur)
  (setq out nil)
  (foreach iv ivs
    (if (null out)
      (setq out (list (list (car iv) (cadr iv))))
      (progn
        (setq cur (car out))
        (if (<= (car iv) (+ (cadr cur) gap))
          (setq out (cons (list (car cur) (max2 (cadr iv) (cadr cur))) (cdr out)))
          (setq out (cons (list (car iv) (cadr iv)) out))))))
  (reverse out))
; cluster lines ((ax lo hi)...) sorted by ax; same cluster while ax - cluster.ax <= tol
; -> ((ax merged-ivs lo hi)...) where lo/hi bound all intervals of the cluster
(defun v49-cluster (lines tol / sorted cls cur out m)
  (setq sorted (v49-sort lines) cls nil)
  (foreach ln sorted
    (setq cur (car cls))
    (if (and cur (<= (- (car ln) (car cur)) tol))
      (setq cls (cons (list (car cur) (cons (list (cadr ln) (caddr ln)) (cadr cur))) (cdr cls)))
      (setq cls (cons (list (car ln) (list (list (cadr ln) (caddr ln)))) cls))))
  (setq out nil)
  (foreach c cls
    (setq m (v49-merge-ivs (v49-sort (cadr c)) v49-gaptol))
    (setq out (cons (list (car c) m (car (car m)) (cadr (car (reverse m)))) out)))
  (reverse out))
; keep clusters whose span >= minspan, ranked by span desc, top nkeep
(defun v49-top (cls minspan nkeep / rank out p)
  (setq rank nil)
  (foreach c cls
    (setq p (- (nth 3 c) (nth 2 c)))
    (if (>= p minspan) (setq rank (cons (list (- p) c) rank))))
  (setq out nil)
  (foreach r (v49-sort rank)
    (if (< (length out) nkeep) (setq out (cons (cadr r) out))))
  (reverse out))
; coverage: union of interval intersections with [t1,t2] >= v49-cov * (t2-t1).
; Segments MAY overshoot the span (real frames often do; the old strict
; inside-bounds check rejected frames with >2mm corner overshoot). False
; candidates from cross pairs are suppressed by the largest-area rule.
(defun v49-cover (ivs t1 t2 / total s e)
  (setq total 0.0)
  (foreach iv ivs
    (setq s (max2 (car iv) t1) e (min2 (cadr iv) t2))
    (if (> e s) (setq total (+ total (- e s)))))
  (>= total (* v49-cov (- t2 t1))))

; largest rectangle: pair of vertical clusters + horizontal clusters covering the span
(defun v49-find-rect4 (/ vls hls i j A B C x1 x2 ylo yhi cy1 cy2 area best)
  (setq best nil)
  (if (and (<= (length vlines) 200) (<= (length hlines) 200))
    (progn
      (setq vls (v49-top (v49-cluster vlines v49-clus-tol) v49-minshort 40))
      (setq hls (v49-top (v49-cluster hlines v49-clus-tol) v49-minshort 40))
      (setq i 0)
      (while (< i (length vls))
        (setq A (nth i vls) j (1+ i))
        (while (< j (length vls))
          (setq B (nth j vls))
          (setq x1 (min2 (car A) (car B)) x2 (max2 (car A) (car B)))
          (if (> (- x2 x1) v49-minshort)
            (progn
              (setq ylo (- (min2 (nth 2 A) (nth 2 B)) 5.0))
              (setq yhi (+ (max2 (nth 3 A) (nth 3 B)) 5.0))
              (setq cy1 nil cy2 nil)
              (foreach C hls
                (if (and (>= (car C) ylo) (<= (car C) yhi) (v49-cover (cadr C) x1 x2))
                  (progn
                    (if (or (null cy1) (< (car C) cy1)) (setq cy1 (car C)))
                    (if (or (null cy2) (> (car C) cy2)) (setq cy2 (car C))))))
              (if (and cy1 cy2 (> (- cy2 cy1) v49-minshort))
                (if (and (v49-cover (cadr A) cy1 cy2) (v49-cover (cadr B) cy1 cy2))
                  (progn
                    (setq area (* (- x2 x1) (- cy2 cy1)))
                    (if (or (null best) (> area (v49-area best)))
                      (setq best (list x1 cy1 x2 cy2))))))))
          (setq j (1+ j)))
        (setq i (1+ i)))))
  best)

; replica of C# FrameValidator rules (non-company / non-BLOCK sources)
(defun v49-autofail (kv / lay w h shortE longE ratio)
  (if (null kv)
    T
    (progn
      (setq lay (car kv) w (- (cadddr kv) (cadr kv)) h (- (nth 4 kv) (caddr kv)))
      (setq shortE (min2 (abs w) (abs h)) longE (max2 (abs w) (abs h)))
      (setq ratio (if (> shortE 0.0) (/ longE shortE) 999.0))
      (cond
        ((wcmatch lay "BLOCK:*") nil)
        ((< shortE 100.0) T)
        ((> ratio 3.0) T)
        ((and (> shortE 1600.0) (or (< ratio 1.27) (> ratio 1.56))) T)
        ((> shortE 2100.0) T)
        ((> longE 3000.0) T)
        (T nil)))))
; v4.12 修订：判定"某个盒本身是否构成合理图纸幅面"（用于把"全图范围"当成图框时的兜底校验）。
; 与 v49-autofail 同阈值，但方向相反：返回 T = 合理可采信。
(defun v49-plausible (b / w h shortE longE ratio)
  (if (null b)
    nil
    (progn
      (setq w (abs (- (caddr b) (car b))) h (abs (- (nth 3 b) (cadr b))))
      (setq shortE (min2 w h) longE (max2 w h))
      (setq ratio (if (> shortE 0.0) (/ longE shortE) 999.0))
      (cond
        ((< shortE 100.0) nil)
        ((> ratio 3.0) nil)
        ((and (> shortE 1600.0) (or (< ratio 1.27) (> ratio 1.56))) nil)
        ((> shortE 2100.0) nil)
        ((> longE 3000.0) nil)
        (T T)))))
; v4.12 修订：判断 bseen（块名 . 世界盒）中是否存在"与给定盒基本重合"的块。
; 用途：只有"整幅范围本身就是一个块"时，才敢用全图范围当图框——
; 否则图框外的内容可能只是散放的图片/表格（实测 F610M 通风袋图纸左侧有 3D 图片，
; 直接按全图范围出图会触发"超大图框兜底取最小纸"→ 整张被压到 A4）。
(defun v49-block-covers (gb blist / tol hit be bb)
  (setq tol (max 25.0 (* 0.03 (max (- (caddr gb) (car gb)) (- (cadddr gb) (cadr gb))))))
  (setq hit nil)
  (foreach be blist
    (setq bb (cadr be))
    (if (and bb
             (v49-near (car bb) (car gb) tol)
             (v49-near (cadr bb) (cadr gb) tol)
             (v49-near (caddr bb) (caddr gb) tol)
             (v49-near (cadddr bb) (cadddr gb) tol))
      (setq hit T)))
  hit)
; v4.12 修订：全图范围必须能被最大纸型(4A0 全出血 1682x2378)容纳（窗口再外扩 M=5mm 两侧），
; 否则 PageSpec 会走"超大图框兜底取最小纸"→ 出图被压成 A4。不满足则放弃采用全图范围。
(defun v49-fit4a0 (b / w h s l)
  (setq w (abs (- (caddr b) (car b))) h (abs (- (nth 3 b) (cadr b))))
  (setq s (min2 w h) l (max2 w h))
  (and (<= l 2368.0) (<= s 1672.0)))
; clean = full extents do not exceed the rectangle by more than
; max(25mm, 3% of the longer rect edge) per side (tolerates line overshoot at corners)
(defun v49-clean (rect gb / tol)
  (setq tol (max 25.0 (* 0.03 (max (- (caddr rect) (car rect)) (- (cadddr rect) (cadr rect))))))
  (and gb
       (>= (car gb) (- (car rect) tol))
       (>= (cadr gb) (- (cadr rect) tol))
       (<= (caddr gb) (+ (caddr rect) tol))
       (<= (cadddr gb) (+ (cadddr rect) tol))))
(defun v49-outline (rect lay src)
  (strcat (rtos (car rect) 2 6) " " (rtos (cadr rect) 2 6) " "
          (rtos (caddr rect) 2 6) " " (rtos (nth 3 rect) 2 6)
          "|" lay "|" src "\n"))

; v4.3 辅助：在 haystack 中从 start 开始找 needle 子串位置
(defun idx-of (needle haystack start / i nlen hlen hit)
  (setq i start hit nil)
  (if (and needle haystack (> (strlen needle) 0))
    (progn
      (setq nlen (strlen needle) hlen (strlen haystack))
      (while (and (null hit) (<= (+ i nlen -1) hlen))
        (if (= (substr haystack i nlen) needle) (setq hit i))
        (setq i (1+ i)))))
  hit)
; v4.3 辅助：判断 bname 是否在 extraBlocks 列表（逗号分隔，"0"=空）
(defun name-in-extras (bname extraBlocks / pos comma part found)
  (if (or (null extraBlocks) (= extraBlocks "0") (= (strlen extraBlocks) 0))
    nil
    (progn
      (setq pos 1 found nil)
      (while (and (<= pos (strlen extraBlocks)) (null found))
        (setq comma (idx-of "," extraBlocks pos))
        (if (null comma) (setq comma (+ (strlen extraBlocks) 1)))
        (setq part (substr extraBlocks pos (- comma pos)))
        (if (and (> (strlen part) 0) (= part bname)) (setq found T))
        (setq pos (+ comma 1)))
      found)))
; v4.3 辅助：判定块内是否含"图框特征"（闭合 LWPOLYLINE bbox 短边 >= 50mm）
(defun block-has-frame? (closed-boxes / box w h short best)
  (if (or (null closed-boxes) (= (length closed-boxes) 0))
    nil
    (progn
      (setq best T)
      (foreach box closed-boxes
        (setq w (- (caddr box) (car box)))
        (setq h (- (nth 3 box) (cadr box)))
        (setq short (min2 w h))
        (if (< short 50.0) (setq best nil)))
      best)))

(defun pts->box (pts / box)
  (setq box nil)
  (foreach p pts
    (if (and p (>= (length p) 2))
      (if box
        (setq box (list (min2 (car box) (car p)) (min2 (cadr box) (cadr p))
                        (max2 (caddr box) (car p)) (max2 (cadddr box) (cadr p))))
        (setq box (list (car p) (cadr p) (car p) (cadr p))))))
  box)
(defun get-pts (d / r g)
  (setq r nil)
  (foreach g d
    (if (and (>= (car g) 10) (<= (car g) 14))
      (setq r (cons (cdr g) r))))
  r)
; v4.14 (A3): ent-pts removed - LINE/LWPOLYLINE are handled inline in the
; main loop (single DXF parse); other types never contributed a layer box.
(defun merge-box (layers layer b / lst r)
  (setq lst (assoc layer layers))
  (setq r (if lst
            (list (min2 (cadr lst) (car b)) (min2 (caddr lst) (cadr b))
                  (max2 (cadddr lst) (caddr b)) (max2 (nth 4 lst) (nth 3 b)))
            (list (car b) (cadr b) (caddr b) (nth 3 b))))
  (if lst (subst (cons layer r) lst layers) (cons (cons layer r) layers)))

; ===== v4.2 upgrade: prefer layer whose short edge matches ISO A-series paper =====
; A-series short edges (mm): A4=210 A3=297 A2=420 A1=594 A0=841 A0plus=1189 2A0=1682 4A0=2378
(setq v42-std-len (list 210.0 297.0 420.0 594.0 841.0 1189.0 1682.0 2378.0))
(setq v42-tol 25.0)
(setq v42-min 100.0)
(defun v42-stdmatch (w / best diff s)
  (setq best 9999.0)
  (foreach s v42-std-len
    (setq diff (abs (- w s)))
    (if (< diff best) (setq best diff)))
  best)
; kv = (layer minx miny maxx maxy). score: -1 = too small (local detail);
; A-series hit -> 10000+area; otherwise area (old behavior fallback).
(defun v42-score (kv / w h short d area)
  (setq w (- (cadddr kv) (cadr kv)))
  (setq h (- (nth 4 kv) (caddr kv)))
  (setq short (min2 w h))
  (setq d (v42-stdmatch short))
  (setq area (* (max 0.0 w) (max 0.0 h)))
  (if (< short v42-min)
    -1.0
    (if (<= d v42-tol)
      (+ 10000.0 area)
      area)))
(defun add-pt (box x y)
  (if box
    (list (min2 (car box) x) (min2 (cadr box) y)
          (max2 (caddr box) x) (max2 (cadddr box) y))
    (list x y x y)))
(defun block-box (bname insX insY sX sY / sub-ent sub sub-d sub-t0 sub-box pt p1 p2 closed pt-list cb)
  ; v4.3：返回 (all-pts-box closed-boxes-list)；调用方判定图框特征
  (setq sub-box nil closed nil)
  (if (setq sub-ent (tblobjname "BLOCK" bname))
    (progn
      (setq sub (entnext sub-ent))
      (while (and sub (/= (cdr (assoc 0 (entget sub))) "ENDBLK"))
        (setq sub-d (entget sub))
        (setq sub-t0 (cdr (assoc 0 sub-d)))
        (cond
          ((= sub-t0 "LWPOLYLINE")
           (progn
             (setq pt-list (get-pts sub-d))
             (if pt-list
               (progn
                 (foreach pt pt-list
                   (if (and pt (>= (length pt) 2))
                     (setq sub-box (add-pt sub-box (+ insX (* sX (car pt))) (+ insY (* sY (cadr pt)))))))
                 (if (and (assoc 70 sub-d) (= (logand (cdr (assoc 70 sub-d)) 1) 1))
                   (progn
                     (setq cb (pts->box pt-list))
                     (if cb (setq closed (cons cb closed)))))))))
          ((= sub-t0 "LINE")
           (progn
             (setq p1 (cdr (assoc 10 sub-d)))
             (if (and p1 (>= (length p1) 2))
               (setq sub-box (add-pt sub-box (+ insX (* sX (car p1))) (+ insY (* sY (cadr p1))))))
             (setq p2 (cdr (assoc 11 sub-d)))
             (if (and p2 (>= (length p2) 2))
               (setq sub-box (add-pt sub-box (+ insX (* sX (car p2))) (+ insY (* sY (cadr p2)))))))))
        (setq sub (entnext sub)))))
  (list sub-box closed))

; ===== v4.10 BLOCKHINT helper =====
; per-name cached bbox of a block DEFINITION (block coordinate space);
; only LWPOLYLINE/LINE content is considered (same scope as block-box)
(setq v49-bdefcache nil)
(defun v49-bdef-box (bname / hit sub-ent sub sub-d t0 pt-list pt box p1 p2)
  (setq hit (assoc bname v49-bdefcache))
  (cond
    (hit (cdr hit))
    (T
     (setq box nil)
     (if (setq sub-ent (tblobjname "BLOCK" bname))
       (progn
         (setq sub (entnext sub-ent))
         (while (and sub (/= (cdr (assoc 0 (entget sub))) "ENDBLK"))
           (setq sub-d (entget sub))
           (setq t0 (cdr (assoc 0 sub-d)))
           (cond
             ((= t0 "LWPOLYLINE")
              (progn
                (setq pt-list (get-pts sub-d))
                (foreach pt pt-list
                  (if (and pt (>= (length pt) 2))
                    (setq box (add-pt box (car pt) (cadr pt)))))))
             ((= t0 "LINE")
              (progn
                (setq p1 (cdr (assoc 10 sub-d)))
                (if (and p1 (>= (length p1) 2))
                  (setq box (add-pt box (car p1) (cadr p1))))
                (setq p2 (cdr (assoc 11 sub-d)))
                (if (and p2 (>= (length p2) 2))
                  (setq box (add-pt box (car p2) (cadr p2)))))))
           (setq sub (entnext sub)))))
     (setq v49-bdefcache (cons (cons bname box) v49-bdefcache))
     box)))

(defun extframe (outfile blockMode extraBlocks / fp ss i n ent d t0 layer box layers any legacy best bmax area score bname ins sX sY sub-box bb closed forced gbox vlines hlines prects rect4 rect4line rect4outline rect4take rect4outtake cand emn emx rect4src bseen bdef wbox bhit be bname2 bbox2 resbox reslay ressrc p1 p2 verts flag bx ok dx dy v imgn imge imgbad id de pp si k kn p lo le g76)
  (setvar "CMDECHO" 0)
  (setq fp (open outfile "w"))
  (setq ss (ssget "_X" (list (cons 410 "Model"))))
  (setq layers nil any nil i 0) (setq gbox nil vlines nil hlines nil prects nil)
  (if ss (setq n (sslength ss)))
  (if (null blockMode) (setq blockMode "0"))
  (if (null extraBlocks) (setq extraBlocks "0"))
  (while (and ss (< i n))
    (setq ent (ssname ss i))
    (setq d (entget ent))
    (setq t0 (cdr (assoc 0 d)))
    (if (= t0 "INSERT")
      (progn
        ; v4.10 BLOCKHINT: record instance bbox of every referenced block (world coords),
        ; deduped by name keeping the largest instance; definition walk is cached per name
        (setq bname (cdr (assoc 2 d)))
        (setq ins (cdr (assoc 10 d)))
        (setq sX (cdr (assoc 41 d)) sY (cdr (assoc 42 d)))
        (if (null sX) (setq sX 1.0))
        (if (null sY) (setq sY 1.0))
        (setq bdef (v49-bdef-box bname))
        (if (and bdef ins (>= (length ins) 2))
          (progn
            (setq wbox (list (+ (car ins) (* sX (car bdef))) (+ (cadr ins) (* sY (cadr bdef)))
                             (+ (car ins) (* sX (caddr bdef))) (+ (cadr ins) (* sY (cadddr bdef)))))
            (setq wbox (list (min2 (car wbox) (caddr wbox)) (min2 (cadr wbox) (cadddr wbox))
                             (max2 (car wbox) (caddr wbox)) (max2 (cadr wbox) (cadddr wbox))))
            (setq bhit (assoc bname bseen))
            (if (null bhit)
              (setq bseen (cons (list bname wbox) bseen))
              (if (> (v49-area wbox) (v49-area (cadr bhit)))
                (setq bseen (subst (list bname wbox) bhit bseen))))))
        ; v4.3 NEW: block-frame recognition ON when blockMode="1"
        (if (= blockMode "1")
          (progn
            (setq bb (block-box bname (car ins) (cadr ins) sX sY))
            (setq sub-box (car bb))
            (setq closed (cadr bb))
            ; v4.3: 图框特征过滤 - extraBlocks 显式含此块名强制视为图框; 否则需块内含图框特征
            (setq forced (name-in-extras bname extraBlocks))
            (if (and sub-box (or forced (block-has-frame? closed)))
              (progn
                (setq any T)
                (setq layers (merge-box layers (strcat "BLOCK:" bname) sub-box))))))
        (setq box nil))
      (progn
        ; v4.14 (A3): LINE/LWPOLYLINE inline - layer box and scan share ONE DXF
        ; parse (old code parsed twice: ent-pts + v49-scan). Other types skip
        ; the useless assoc-8 / ent-pts roundtrip.
        (cond
          ((= t0 "LINE")
           (setq p1 (cdr (assoc 10 d)) p2 (cdr (assoc 11 d)))
           (if (and p1 p2)
             (progn
               (setq layer (cdr (assoc 8 d)))
               (if (null layer) (setq layer "<none>"))
               (setq box (pts->box (list p1 p2)))
               (if box
                 (progn
                   (setq any T)
                   (setq layers (merge-box layers layer box))))
               (if (and (>= (length p1) 2) (>= (length p2) 2))
                 (progn
                   (setq dx (abs (- (car p2) (car p1))) dy (abs (- (cadr p2) (cadr p1))))
                   (if (and (> dx v49-minshort) (< dy 0.01))
                     (setq hlines (cons (list (cadr p1) (min2 (car p1) (car p2)) (max2 (car p1) (car p2))) hlines)))
                   (if (and (> dy v49-minshort) (< dx 0.01))
                     (setq vlines (cons (list (car p1) (min2 (cadr p1) (cadr p2)) (max2 (cadr p1) (cadr p2))) vlines))))))))
          ((= t0 "LWPOLYLINE")
           (setq verts (get-pts d))
           (if verts
             (progn
               (setq layer (cdr (assoc 8 d)))
               (if (null layer) (setq layer "<none>"))
               (setq box (pts->box verts))
               (if box
                 (progn
                   (setq any T)
                   (setq layers (merge-box layers layer box))))
               (setq flag (cdr (assoc 70 d)))
               (if (and verts (>= (length verts) 4) (<= (length verts) 5) flag (= (logand flag 1) 1))
                 (progn
                   (setq bx (pts->box verts))
                   (if bx
                     (progn
                       (setq ok T)
                       (foreach v verts
                         (if (and ok
                                  (not (and (or (v49-near (car v) (car bx) 5.0) (v49-near (car v) (caddr bx) 5.0))
                                            (or (v49-near (cadr v) (cadr bx) 5.0) (v49-near (cadr v) (cadddr bx) 5.0)))))
                           (setq ok nil)))
                       (if (and ok (>= (v49-short bx) v49-minshort))
                         (setq prects (cons bx prects))))))))))
          (T nil))))
    (setq i (1+ i)))
  ; v4.9: RECT4 decision - only when the legacy/auto candidate would fail validation
  ; v4.9.1: authoritative zoom-e extents replace point-based gbox (point collection
  ; overestimates ARC/CIRCLE bboxes -> false RECT4_OUT). Fallback: keep scanned gbox.
(if any
    (progn
      (command "._ZOOM" "_E")
      (setq emn (getvar "EXTMIN") emx (getvar "EXTMAX"))
      (if (and emn emx
               (>= (car emx) (car emn)) (>= (cadr emx) (cadr emn))
               (or (> (- (car emx) (car emn)) 1.0) (> (- (cadr emx) (cadr emn)) 1.0)))
        (setq gbox (list (car emn) (cadr emn) (car emx) (cadr emx)))
        (setq gbox (v49-gbox-rescan ss)))))  (setq rect4 nil rect4take nil rect4outtake nil rect4line nil rect4outline nil)
  (setq rect4 (v49-find-rect4) rect4src "LINES")
  (foreach pb prects
    (if (and (>= (v49-short pb) v49-minshort)
             (or (null rect4) (> (v49-area pb) (v49-area rect4))))
      (setq rect4 pb rect4src "PRECT")))
  (if rect4
    (progn
      (setq legacy (assoc "图层图框" layers))
      (setq cand
        (if (and legacy (<= (v42-stdmatch (min2 (- (cadddr legacy) (cadr legacy)) (- (nth 4 legacy) (nth 3 legacy)))) v42-tol))
          legacy
          (progn
            (setq bmax -1.0 best nil)
            (foreach kv layers
              (setq score (v42-score kv))
              (if (> score bmax) (setq bmax score best kv)))
            best)))
      (if (v49-autofail cand)
        (if (or (= rect4src "PRECT") (v49-clean rect4 gbox))
          (setq rect4take T rect4line (v49-outline rect4 "RECT4" "RECT4"))
          (setq rect4outtake T rect4outline (v49-outline rect4 "RECT4" "RECT4_OUT"))))))
  ; v4.12 修订：始终输出"全图范围"（zoom-e 范围），供 C# 与人工核对识别框是否覆盖全图
  (if (and fp gbox)
    (princ (strcat "EXTENT|" (rtos (car gbox) 2 6) " " (rtos (cadr gbox) 2 6) " "
                   (rtos (caddr gbox) 2 6) " " (rtos (nth 3 gbox) 2 6) "\n") fp))
  ; v4.15：含图片图纸检测（ssget 类型过滤器 + 只认"图片型" OLE，不额外遍历全部实体）。
  ;   IMAGE                = 光栅图像
  ;   OLE2FRAME(组码3含Picture) = 粘贴的图片（实测：Paintbrush Picture /
  ;                          Picture (Device Independent Bitmap)）；表格/文本型 OLE 不计入，
  ;                          避免把普通图纸也送进 COM 高清路径（会改变渲染且更慢）
  ;   *UNDERLAY            = PDF/DWF/DGN 参考底图（官方文档指出其光栅内容会低质量输出）
  ; 供出图阶段选择"改走 COM + 高清打印配置"（accoreconsole 的 -EXPORT 不认打印质量）。
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
  ; v4.12 修订：先统一算出 结果盒/结果层/结果来源（便于后面做完整性修正）
  (setq resbox nil reslay nil ressrc nil)
  (cond
    ((not any)
     (setq ressrc "ERR"))
    (rect4take
     (setq resbox rect4 reslay "RECT4" ressrc "RECT4"))
    (rect4outtake
     (setq resbox rect4 reslay "RECT4" ressrc "RECT4_OUT"))
    (T
     (progn
       (setq legacy (assoc "图层图框" layers))
       (if (and legacy (<= (v42-stdmatch (min2 (- (cadddr legacy) (cadr legacy)) (- (nth 4 legacy) (nth 3 legacy)))) v42-tol))
         (setq resbox (list (cadr legacy) (caddr legacy) (cadddr legacy) (nth 4 legacy))
               reslay "图层图框" ressrc "legacy")
         (progn
           (setq bmax -1.0)
           (foreach kv layers
             (setq score (v42-score kv))
             (if (> score bmax) (setq bmax score best kv)))
           (if best
             (setq resbox (list (cadr best) (caddr best) (cadddr best) (nth 4 best))
                   reslay (car best) ressrc "auto")
             (setq ressrc "ERR")))))))
  ; v4.12 修订 完整性修正：auto/legacy 结果没有覆盖全图范围时，只有同时满足下面 3 个条件才改用全图范围：
  ;   ① 全图范围本身是合理图纸幅面（v49-plausible：非散线撑大 / 比例正常）
  ;   ② 全图范围有"块佐证"（bseen 中存在与全图范围基本重合的块）——说明整幅图框是一个块
  ;   ③ 全图范围能被最大纸型 4A0 容纳（否则会触发"超大图框兜底取最小纸"被压成 A4）
  ; 实测：H47 总成图（整幅=块 EQWE）满足 ①②③ → 修复被裁 240x137mm；
  ;       F610M 通风袋（图框外是散放 3D 图片）只有 ① → 保持原识别框并输出 WARN。
  (if (and resbox (or (= ressrc "auto") (= ressrc "legacy")) (not (v49-clean resbox gbox)))
    (if (and (v49-plausible gbox) (v49-block-covers gbox bseen) (v49-fit4a0 gbox))
      (progn
        (if fp (princ (strcat "CANDIDATE|" (rtos (car resbox) 2 6) " " (rtos (cadr resbox) 2 6) " "
                              (rtos (caddr resbox) 2 6) " " (rtos (nth 3 resbox) 2 6) "\n") fp))
        (setq resbox gbox reslay "EXTENT" ressrc "EXTENT"))
      (if fp (princ "WARN|extent-out\n" fp))))
  ; 主结果输出（一个文件只有一条主结果行；"层|来源"格式与 C# ParseResult 一致）
  (if (= ressrc "ERR")
    (if fp (princ "-1|ERR|nobbox\n" fp))
    (if resbox
      (if fp (princ (strcat (rtos (car resbox) 2 6) " " (rtos (cadr resbox) 2 6) " "
                            (rtos (caddr resbox) 2 6) " " (rtos (nth 3 resbox) 2 6)
                            "|" reslay "|" ressrc "\n") fp))))
  ; v4.10 BLOCKHINT: report big blocks that might be the actual frame
  ; (short edge >= 500mm and, when a rect was identified, area >= 1.05x rect area)
  (foreach be bseen
    (setq bname2 (car be) bbox2 (cadr be))
    (if (and bbox2
             (>= (v49-short bbox2) 500.0)
             (or (null rect4) (>= (v49-area bbox2) (* 1.05 (v49-area rect4)))))
      (if fp
        (princ (strcat "BLOCKHINT|" bname2 "|"
                       (rtos (car bbox2) 2 6) " " (rtos (cadr bbox2) 2 6) " "
                       (rtos (caddr bbox2) 2 6) " " (rtos (cadddr bbox2) 2 6) "\n") fp))))
  (if fp (close fp))
  (princ))
