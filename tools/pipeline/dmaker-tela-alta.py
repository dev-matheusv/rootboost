# Roda o dmaker com a tela do sistema mais alta no quadro, para a legenda caber abaixo dela e acima da barra
# do Instagram. O DMaker sempre centraliza; aqui so desloca o retangulo, sem mexer no repositorio dele.
import os, sys
SOBE = int(os.environ.get("TELA_SOBE", "170"))
import dmaker.visuals.geometry as g
original = g.fit_rect
def fit_rect(*a, **k):
    x, y, w, h = original(*a, **k)
    return x, y - SOBE, w, h
import dmaker.filtergraph.reframe as r, dmaker.pipeline.segments as s
r.fit_rect = fit_rect
s.fit_rect = fit_rect
from dmaker.cli import app
sys.argv[0] = "dmaker"
app()
