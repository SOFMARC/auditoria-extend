robocopy "..\Publish\BPOContasSaude" "\\cab-srv-350\wwwroot$\BPOContasSaude" /MIR /xf *.config /xd Log /LOG+:LogCopiaBPOContasSaudeDesenv_350.txt
robocopy "..\Publish\easyremessa"    "\\cab-srv-350\wwwroot$\easyremessa"    /MIR /xf *.config /xd Log /LOG+:LogCopiaEasyRemessaDesenv_350.txt
robocopy "..\Publish\WSRemessa"      "\\cab-srv-350\wwwroot$\WSRemessa"      /MIR /xf *.config /xd Log /LOG+:LogCopiaWSRemessaDesenv_350.txt

echo.
echo.
if /I not "%1"=="/np" (
  pause
)
