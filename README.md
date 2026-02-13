---
title: "README"
author: "kenomysz"
date: "13.02.2026"
lang: pl
geometry: "left=2cm,right=2cm,top=2cm,bottom=2cm"
fontsize: 11pt
header-includes:
  - \usepackage{fvextra}
  - \DefineVerbatimEnvironment{Highlighting}{Verbatim}{breaklines,commandchars=\\\{\}}
---

# **PhotoSorter**

# Features
PhotoSorter monitors recursively monitors a `Source` folder, including TAR/ZIP archives. It sorts photos by time and location using metadata, which is done by moving into subdirectories of `Output` as follows:
```
Output/year/month/day/country/city
```
