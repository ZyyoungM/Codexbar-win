
  # CodexBar for Windows Design

  This is a code bundle for CodexBar for Windows Design. The original project is available at https://www.figma.com/design/gPp73veJaeJ9PQfsRPfcBk/CodexBar-for-Windows-Design.

  ## Running the code

  Run `npm i` to install the dependencies.

  Run `npm run dev` to start the development server.

  ## Integration rules in codexbar-win

  - This folder is the imported Figma frontend baseline for the rebuild start.
  - Keep `src/app/App.tsx` and `src/app/components/*` visually faithful to the Figma export.
  - Do not turn the tray-first desktop UI into a left-nav web dashboard.
  - Do not convert `Settings`, `OAuth`, `Add Compatible Provider`, or `Edit Account` into route pages.
  - Future native hosting or API wiring must wrap this baseline rather than redesign it first.
  
