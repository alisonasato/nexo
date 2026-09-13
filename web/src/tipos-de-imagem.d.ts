/*
 * A declaração dos imports de imagem, escrita à mão.
 *
 * Quem normalmente a traz é o `next-env.d.ts`, que o Next gera e que fica fora
 * do repositório. Num clone limpo ele não existe até o primeiro build, e a
 * verificação de tipos da CI roda antes do build: sem esta linha, importar
 * `montanhas.jpg` falha por falta de declaração do módulo. É a mesma armadilha
 * que fez o layout raiz declarar as props à mão.
 */
/// <reference types="next/image-types/global" />
