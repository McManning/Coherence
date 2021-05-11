
From root:

```
Documentation~/make.bat clean && sphinx-autobuild --port 3031 --watch Blender~/Addon/ Documentation~ _build
```

All the dependencies have been pinned back in requirements.txt a bit because the CSS is kind of garbage for method names / class names in sphinx 4.0.1 and whatever the latest rtd theme is.
