using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

internal static class GlShaders
{
    public const string FullscreenVs = """
                                       #version 330 core
                                       layout(location = 0) in vec2 aPos;
                                       out vec2 vUv;
                                       void main() {
                                           vUv = aPos * 0.5 + 0.5;
                                           gl_Position = vec4(aPos, 0.0, 1.0);
                                       }
                                       """;

    public const string PresentFs = """
                                    #version 330 core
                                    in vec2 vUv;
                                    uniform sampler2D uVram;
                                    uniform vec2 uOrigin;
                                    uniform vec2 uSize;
                                    uniform vec2 uTexSize;
                                    out vec4 oColor;
                                    void main() {
                                        vec2 t = (uOrigin + vUv * uSize) / uTexSize;
                                        oColor = vec4(texture(uVram, t).rgb, 1.0);
                                    }
                                    """;

    public const string Present24Fs = """
                                      #version 330 core
                                      in vec2 vUv;
                                      uniform sampler2D uVram;
                                      uniform vec2 uOrigin;
                                      uniform vec2 uSize;
                                      uniform int uScale;
                                      out vec4 oColor;

                                      int u5(float f) { return int(floor(f * 31.0 + 0.5)); }
                                      int texel16(int lin) {
                                          vec4 p = texelFetch(uVram, ivec2((lin & 1023) * uScale, ((lin >> 10) & 511) * uScale), 0);
                                          return u5(p.r) | (u5(p.g) << 5) | (u5(p.b) << 10) | (int(ceil(p.a)) << 15);
                                      }
                                      int byteAt(int b) {
                                          int t = texel16(b >> 1);
                                          return (b & 1) == 0 ? (t & 0xff) : ((t >> 8) & 0xff);
                                      }
                                      void main() {
                                          int px = int(floor(vUv.x * uSize.x));
                                          int py = int(floor(vUv.y * uSize.y));
                                          int ty = int(uOrigin.y) + py;
                                          int base = (ty * 1024 + int(uOrigin.x)) * 2 + px * 3;
                                          oColor = vec4(float(byteAt(base)) / 255.0, float(byteAt(base + 1)) / 255.0,
                                                        float(byteAt(base + 2)) / 255.0, 1.0);
                                      }
                                      """;

    public const string PrimVs = """
                                 #version 330 core
                                 layout(location = 0) in vec2  inPos;
                                 layout(location = 1) in vec3  inColorF;
                                 layout(location = 2) in float inClutF;
                                 layout(location = 3) in float inTexpageF;
                                 layout(location = 4) in vec2  inUV;

                                 out vec4 vColor;
                                 out vec2 vUV;
                                 flat out ivec2 clutBase;
                                 flat out ivec2 pageBase;
                                 flat out int   texMode;
                                 flat out int   vDither;
                                 flat out int   vRepClut;

                                 uniform vec2 uVertexOffset;
                                 uniform vec2 uPosBias;
                                 uniform vec2 uFbInv;

                                 void main() {
                                     vec2 p = (inPos + uVertexOffset + uPosBias) * uFbInv - 1.0;
                                     gl_Position = vec4(p, 0.0, 1.0);

                                     int inClut = int(inClutF + 0.5);
                                     int inTexpage = int(inTexpageF + 0.5);

                                     vColor = vec4(inColorF, 0.0) / 255.0;
                                     vDither = (inTexpage >> 10) & 1;
                                     vRepClut = (inTexpage >> 12) & 1;

                                     if ((inTexpage & 0x8000) != 0) {
                                         texMode = 4;
                                     } else if ((inTexpage & 0x4000) != 0) {
                                         texMode = 5;
                                         vUV = inUV;
                                     } else if ((inTexpage & 0x2000) != 0) {
                                         texMode = 6;
                                         vUV = inUV;
                                     } else {
                                         texMode = (inTexpage >> 7) & 3;
                                         vUV = inUV;
                                         pageBase = ivec2((inTexpage & 0xf) * 64, ((inTexpage >> 4) & 1) * 256);
                                         clutBase = ivec2((inClut & 0x3f) * 16, (inClut >> 6) & 0x1ff);
                                     }
                                 }
                                 """;

    public const string PrimFs = """
                                 #version 330 core
                                 in vec4 vColor;
                                 in vec2 vUV;
                                 flat in ivec2 clutBase;
                                 flat in ivec2 pageBase;
                                 flat in int   texMode;
                                 flat in int   vDither;
                                 flat in int   vRepClut;

                                 layout(location = 0, index = 0) out vec4 FragColor;
                                 layout(location = 0, index = 1) out vec4 BlendColor;

                                 uniform sampler2D uVram;
                                 uniform sampler2D uDest;
                                 uniform sampler2D uExtTex;
                                 uniform sampler2D uRepTex;
                                 uniform sampler2D uRepClut;
                                 uniform vec4  uRepRect;
                                 uniform float uRepClutCount;
                                 uniform ivec4 uTexWindow;
                                 uniform vec4  uBlend;
                                 uniform vec4  uBlendOpaque = vec4(1.0, 1.0, 1.0, 0.0);
                                 uniform float uSetMask;
                                 uniform int   uCheckMask;
                                 uniform int   uScale;
                                 uniform vec2  uPosBias;

                                 const int ditherTbl[16] = int[16](
                                     -4,  0, -3,  1,
                                      2, -2,  3, -1,
                                     -3,  1, -4,  0,
                                      3, -1,  2, -2 );

                                 int u5(float f) { return int(floor(f * 31.0 + 0.5)); }
                                 vec4 fetch(ivec2 c) { return texelFetch(uVram, (c & ivec2(1023, 511)) * uScale, 0); }
                                 int fetch16(ivec2 c) {
                                     vec4 p = fetch(c);
                                     return u5(p.r) | (u5(p.g) << 5) | (u5(p.b) << 10) | (int(ceil(p.a)) << 15);
                                 }
                                 vec3 quant5(ivec3 c8) {
                                     if (vDither != 0) {
                                         ivec2 vp = ivec2(floor(gl_FragCoord.xy / float(uScale) - uPosBias));
                                         c8 = clamp(c8 + ditherTbl[(vp.y & 3) * 4 + (vp.x & 3)], 0, 255);
                                     }
                                     return vec3(min(c8 >> 3, 31)) / 31.0;
                                 }

                                 void main() {
                                     if (uCheckMask != 0 && texelFetch(uDest, ivec2(gl_FragCoord.xy), 0).a >= 0.5) discard;

                                     if (texMode == 4) {
                                         FragColor = vec4(quant5(ivec3(vColor.rgb * 255.0 + 0.5)), uSetMask);
                                         BlendColor = uBlend;
                                         return;
                                     }

                                     if (texMode == 5) {
                                         vec4 img = texture(uExtTex, vUV);
                                         if (img.a < 0.5) discard;
                                         ivec3 e8 = (ivec3(img.rgb * 255.0 + 0.5) * ivec3(vColor.rgb * 255.0 + 0.5)) >> 7;
                                         FragColor = vec4(quant5(e8), uSetMask);
                                         BlendColor = uBlend;
                                         return;
                                     }

                                     int rawU = dFdx(vUV.x) < 0.0 ? int(ceil(vUV.x - 0.0001)) : int(floor(vUV.x + 0.0001));
                                     int rawV = dFdy(vUV.y) < 0.0 ? int(ceil(vUV.y - 0.0001)) : int(floor(vUV.y + 0.0001));
                                     ivec2 uv = (ivec2(rawU, rawV) & uTexWindow.xy) | uTexWindow.zw;
                                     uv &= ivec2(0xff);

                                     if (texMode == 6) {
                                         vec2 win = vec2(uTexWindow.xy) + 1.0;
                                         vec2 fuv = mod(vUV, win) + vec2(uTexWindow.zw);
                                         vec2 t = (fuv - uRepRect.xy) / uRepRect.zw;
                                         vec4 img = texture(uRepTex, t);
                                         if (img.a < 0.5) discard;
                                         ivec3 e8 = (ivec3(img.rgb * 255.0 + 0.5) * ivec3(vColor.rgb * 255.0 + 0.5)) >> 7;
                                         float stp = img.a < 0.95 ? 1.0 : 0.0;
                                         FragColor = vec4(quant5(e8), max(stp, uSetMask));
                                         BlendColor = stp > 0.5 ? uBlend : uBlendOpaque;
                                         return;
                                     }

                                     vec4 texel;

                                     if (texMode == 0) {
                                         int s = fetch16(ivec2(pageBase.x + (uv.x >> 2), pageBase.y + uv.y));
                                         int idx = (s >> ((uv.x & 3) << 2)) & 0xf;
                                         texel = vRepClut != 0
                                             ? texture(uRepClut, vec2((float(idx) + 0.5) / uRepClutCount, 0.5))
                                             : fetch(ivec2(clutBase.x + idx, clutBase.y));
                                     } else if (texMode == 1) {
                                         int s = fetch16(ivec2(pageBase.x + (uv.x >> 1), pageBase.y + uv.y));
                                         int idx = (s >> ((uv.x & 1) << 3)) & 0xff;
                                         texel = vRepClut != 0
                                             ? texture(uRepClut, vec2((float(idx) + 0.5) / uRepClutCount, 0.5))
                                             : fetch(ivec2(clutBase.x + idx, clutBase.y));
                                     } else {
                                         texel = fetch(ivec2(pageBase.x + uv.x, pageBase.y + uv.y));
                                     }

                                     if (vRepClut != 0 && texMode != 2) {
                                         if (texel.a < 0.5) discard;
                                         ivec3 e8 = (ivec3(texel.rgb * 255.0 + 0.5) * ivec3(vColor.rgb * 255.0 + 0.5)) >> 7;
                                         float stp = texel.a < 0.95 ? 1.0 : 0.0;
                                         FragColor = vec4(quant5(e8), max(stp, uSetMask));
                                         BlendColor = stp > 0.5 ? uBlend : uBlendOpaque;
                                         return;
                                     }

                                     if (texel.rgb == vec3(0.0) && texel.a < 0.5) discard;
                                     ivec3 t8 = ivec3(texel.rgb * 31.0 + 0.5) << 3;
                                     ivec3 c8 = (t8 * ivec3(vColor.rgb * 255.0 + 0.5)) >> 7;
                                     FragColor = vec4(quant5(c8), max(texel.a, uSetMask));
                                     BlendColor = texel.a >= 0.5 ? uBlend : uBlendOpaque;
                                 }
                                 """;

    public const string FullscreenVs120 = """
                                          #version 120
                                          attribute vec2 aPos;
                                          varying vec2 vUv;
                                          void main() {
                                              vUv = aPos * 0.5 + 0.5;
                                              gl_Position = vec4(aPos, 0.0, 1.0);
                                          }
                                          """;

    public const string PresentFs120 = """
                                       #version 120
                                       varying vec2 vUv;
                                       uniform sampler2D uVram;
                                       uniform vec2 uOrigin;
                                       uniform vec2 uSize;
                                       uniform vec2 uTexSize;
                                       void main() {
                                           vec2 t = (uOrigin + vUv * uSize) / uTexSize;
                                           gl_FragColor = vec4(texture2D(uVram, t).rgb, 1.0);
                                       }
                                       """;

    public const string Present24Fs120 = """
                                         #version 120
                                         varying vec2 vUv;
                                         uniform sampler2D uVram;
                                         uniform vec2 uOrigin;
                                         uniform vec2 uSize;
                                         uniform vec2 uVramSize;
                                         uniform float uScale;

                                         float u5(float f) { return floor(f * 31.0 + 0.5); }

                                         float texel16(float lin) {
                                             float x = mod(lin, 1024.0);
                                             float y = floor(lin / 1024.0);
                                             vec2 uv = (vec2(x, y) * uScale + 0.5) / uVramSize;
                                             vec4 p = texture2D(uVram, uv);
                                             return u5(p.r) + u5(p.g) * 32.0 + u5(p.b) * 1024.0 + ceil(p.a) * 32768.0;
                                         }

                                         float byteAt(float b) {
                                             float t = texel16(floor(b * 0.5));
                                             return mod(b, 2.0) < 0.5 ? mod(t, 256.0) : floor(t / 256.0);
                                         }

                                         void main() {
                                             float px = floor(vUv.x * uSize.x);
                                             float py = floor(vUv.y * uSize.y);
                                             float ty = uOrigin.y + py;
                                             float base = (ty * 1024.0 + uOrigin.x) * 2.0 + px * 3.0;
                                             gl_FragColor = vec4(byteAt(base) / 255.0, byteAt(base + 1.0) / 255.0, byteAt(base + 2.0) / 255.0, 1.0);
                                         }
                                         """;

    public const string BlitVs120 = """
                                    #version 120
                                    attribute vec2 aPos;
                                    uniform vec4 uDstRect;
                                    uniform vec4 uSrcRect;
                                    varying vec2 vSrc;
                                    void main() {
                                        vec2 unit = aPos * 0.5 + 0.5;
                                        vSrc = uSrcRect.xy + unit * uSrcRect.zw;
                                        vec2 p = uDstRect.xy + unit * uDstRect.zw;
                                        gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
                                    }
                                    """;

    public const string BlitFs120 = """
                                    #version 120
                                    varying vec2 vSrc;
                                    uniform sampler2D uSrc;
                                    void main() { gl_FragColor = texture2D(uSrc, vSrc); }
                                    """;

    public const string PrimVs120 = """
                                    #version 120
                                    attribute vec2  inPos;
                                    attribute vec3  inColorF;
                                    attribute float inClutF;
                                    attribute float inTexpageF;
                                    attribute vec2  inUV;

                                    varying vec4  vColor;
                                    varying vec2  vUV;
                                    varying vec2  vClutBase;
                                    varying vec2  vPageBase;
                                    varying float vTexMode;
                                    varying float vDither;
                                    varying float vRepClut;

                                    uniform vec2 uVertexOffset;
                                    uniform vec2 uPosBias;
                                    uniform vec2 uFbInv;

                                    float bitAt(float v, float bit) { return floor(mod(v / bit, 2.0)); }

                                    void main() {
                                        vec2 p = (inPos + uVertexOffset + uPosBias) * uFbInv - 1.0;
                                        gl_Position = vec4(p, 0.0, 1.0);

                                        float tp = floor(inTexpageF + 0.5);
                                        float clut = floor(inClutF + 0.5);

                                        vColor = vec4(inColorF / 255.0, 0.0);
                                        vDither = bitAt(tp, 1024.0);
                                        vRepClut = bitAt(tp, 4096.0);
                                        vUV = inUV;
                                        vClutBase = vec2(0.0);
                                        vPageBase = vec2(0.0);

                                        if (bitAt(tp, 32768.0) > 0.5) {
                                            vTexMode = 4.0;
                                        } else if (bitAt(tp, 16384.0) > 0.5) {
                                            vTexMode = 5.0;
                                        } else if (bitAt(tp, 8192.0) > 0.5) {
                                            vTexMode = 6.0;
                                        } else {
                                            vTexMode = floor(mod(tp / 128.0, 4.0));
                                            vPageBase = vec2(mod(tp, 16.0) * 64.0, bitAt(tp, 16.0) * 256.0);
                                            vClutBase = vec2(mod(clut, 64.0) * 16.0, mod(floor(clut / 64.0), 512.0));
                                        }
                                    }
                                    """;

    //gl 2.1 has no dual source blending =/ has to do by hand
    public const string PrimFs120 = """
                                    #version 120
                                    varying vec4  vColor;
                                    varying vec2  vUV;
                                    varying vec2  vClutBase;
                                    varying vec2  vPageBase;
                                    varying float vTexMode;
                                    varying float vDither;
                                    varying float vRepClut;

                                    uniform sampler2D uVram;
                                    uniform sampler2D uDest;
                                    uniform sampler2D uExtTex;
                                    uniform sampler2D uRepTex;
                                    uniform sampler2D uRepClut;
                                    uniform vec4  uRepRect;
                                    uniform float uRepClutCount;
                                    uniform vec4  uTexWindow;
                                    uniform float uSetMask;
                                    uniform float uCheckMask;
                                    uniform float uScale;
                                    uniform vec2  uPosBias;
                                    uniform vec2  uVramSize;
                                    uniform vec2  uDestSize;
                                    uniform float uSemiTrans;
                                    uniform float uBlendMode;

                                    float u5(float f) { return floor(f * 31.0 + 0.5); }

                                    vec4 fetch(vec2 c) {
                                        vec2 w = vec2(mod(c.x, 1024.0), mod(c.y, 512.0));
                                        return texture2D(uVram, (w * uScale + 0.5) / uVramSize);
                                    }

                                    float fetch16(vec2 c) {
                                        vec4 p = fetch(c);
                                        return u5(p.r) + u5(p.g) * 32.0 + u5(p.b) * 1024.0 + ceil(p.a) * 32768.0;
                                    }

                                    vec3 quant5(vec3 c8) {
                                        if (vDither > 0.5) {
                                            vec2 vp = floor(gl_FragCoord.xy / uScale - uPosBias);
                                            float col = mod(vp.x, 4.0);
                                            float row = mod(vp.y, 4.0);
                                            float d = 0.0;
                                            if (row < 0.5)      d = col < 0.5 ? -4.0 : (col < 1.5 ?  0.0 : (col < 2.5 ? -3.0 :  1.0));
                                            else if (row < 1.5) d = col < 0.5 ?  2.0 : (col < 1.5 ? -2.0 : (col < 2.5 ?  3.0 : -1.0));
                                            else if (row < 2.5) d = col < 0.5 ? -3.0 : (col < 1.5 ?  1.0 : (col < 2.5 ? -4.0 :  0.0));
                                            else                d = col < 0.5 ?  3.0 : (col < 1.5 ? -1.0 : (col < 2.5 ?  2.0 : -2.0));
                                            c8 = clamp(c8 + d, 0.0, 255.0);
                                        }
                                        return min(floor(c8 / 8.0), 31.0) / 31.0;
                                    }

                                    vec3 blendWith(vec3 src, vec3 dst) {
                                        if (uBlendMode < 0.5) return (dst + src) * 0.5;
                                        if (uBlendMode < 1.5) return dst + src;
                                        if (uBlendMode < 2.5) return dst - src;
                                        return dst + src * 0.25;
                                    }

                                    void main() {
                                        vec2 destUv = gl_FragCoord.xy / uDestSize;
                                        vec4 dstTexel = texture2D(uDest, destUv);
                                        if (uCheckMask > 0.5 && dstTexel.a >= 0.5) discard;

                                        vec3 rgb;
                                        float stp;
                                        float mask;

                                        if (vTexMode > 3.5 && vTexMode < 4.5) {
                                            rgb = vColor.rgb * 255.0;
                                            stp = 1.0;
                                            mask = uSetMask;
                                        } else if (vTexMode > 4.5 && vTexMode < 5.5) {
                                            vec4 img = texture2D(uExtTex, vUV);
                                            if (img.a < 0.5) discard;
                                            rgb = floor(img.rgb * 255.0 + 0.5) * floor(vColor.rgb * 255.0 + 0.5) / 128.0;
                                            stp = 1.0;
                                            mask = uSetMask;
                                        } else {
                                            vec2 win = uTexWindow.xy + 1.0;
                                            vec2 fuv = vec2(mod(vUV.x, win.x), mod(vUV.y, win.y)) + uTexWindow.zw;


                                            float rawU = dFdx(vUV.x) < 0.0 ? ceil(vUV.x - 0.0001) : floor(vUV.x + 0.0001);
                                            float rawV = dFdy(vUV.y) < 0.0 ? ceil(vUV.y - 0.0001) : floor(vUV.y + 0.0001);

                                            if (vTexMode > 5.5) {
                                                vec2 t = (fuv - uRepRect.xy) / uRepRect.zw;
                                                vec4 img = texture2D(uRepTex, t);
                                                if (img.a < 0.5) discard;
                                                rgb = floor(img.rgb * 255.0 + 0.5) * floor(vColor.rgb * 255.0 + 0.5) / 128.0;
                                                stp = img.a < 0.95 ? 1.0 : 0.0;
                                                mask = max(stp, uSetMask);
                                            } else {
                                                vec2 uv = vec2(mod(rawU, win.x), mod(rawV, win.y)) + uTexWindow.zw;
                                                uv = vec2(mod(uv.x, 256.0), mod(uv.y, 256.0));
                                                vec4 texel;

                                                if (vTexMode < 0.5) {
                                                    float s = fetch16(vec2(vPageBase.x + floor(uv.x / 4.0), vPageBase.y + uv.y));
                                                    float lane = mod(uv.x, 4.0);
                                                    float div = lane < 0.5 ? 1.0 : (lane < 1.5 ? 16.0 : (lane < 2.5 ? 256.0 : 4096.0));
                                                    float idx = mod(floor(s / div), 16.0);
                                                    texel = vRepClut > 0.5
                                                        ? texture2D(uRepClut, vec2((idx + 0.5) / uRepClutCount, 0.5))
                                                        : fetch(vec2(vClutBase.x + idx, vClutBase.y));
                                                } else if (vTexMode < 1.5) {
                                                    float s = fetch16(vec2(vPageBase.x + floor(uv.x / 2.0), vPageBase.y + uv.y));
                                                    float div = mod(uv.x, 2.0) < 0.5 ? 1.0 : 256.0;
                                                    float idx = mod(floor(s / div), 256.0);
                                                    texel = vRepClut > 0.5
                                                        ? texture2D(uRepClut, vec2((idx + 0.5) / uRepClutCount, 0.5))
                                                        : fetch(vec2(vClutBase.x + idx, vClutBase.y));
                                                } else {
                                                    texel = fetch(vec2(vPageBase.x + uv.x, vPageBase.y + uv.y));
                                                }

                                                if (vRepClut > 0.5 && vTexMode < 1.5) {
                                                    if (texel.a < 0.5) discard;
                                                    rgb = floor(texel.rgb * 255.0 + 0.5) * floor(vColor.rgb * 255.0 + 0.5) / 128.0;
                                                    stp = texel.a < 0.95 ? 1.0 : 0.0;
                                                } else {
                                                    if (texel.r == 0.0 && texel.g == 0.0 && texel.b == 0.0 && texel.a < 0.5) discard;
                                                    vec3 t8 = floor(texel.rgb * 31.0 + 0.5) * 8.0;
                                                    rgb = t8 * floor(vColor.rgb * 255.0 + 0.5) / 128.0;
                                                    stp = texel.a >= 0.5 ? 1.0 : 0.0;
                                                }
                                                mask = max(stp, uSetMask);
                                            }
                                        }

                                        vec3 outRgb = quant5(floor(rgb));
                                        if (uSemiTrans * stp > 0.5) outRgb = clamp(blendWith(outRgb, dstTexel.rgb), 0.0, 1.0);

                                        gl_FragColor = vec4(outRgb, mask);
                                    }
                                    """;

    private static readonly (uint Index, string Name)[] PrimAttribs =
    [
        (0, "inPos"), (1, "inColorF"), (2, "inClutF"), (3, "inTexpageF"), (4, "inUV")
    ];

    public static uint BuildPrim(GL gl, string vsSrc, string fsSrc, string name)
    {
        return Build(gl, vsSrc, fsSrc, name, PrimAttribs);
    }

    public static uint BuildFullscreen(GL gl, string vsSrc, string fsSrc, string name)
    {
        return Build(gl, vsSrc, fsSrc, name, [(0, "aPos")]);
    }

    public static uint Build(GL gl, string vsSrc, string fsSrc, string name, out string? error)
    {
        error = null;
        var vs = CompileStage(gl, ShaderType.VertexShader, vsSrc, name, out var vsLog);
        var fs = CompileStage(gl, ShaderType.FragmentShader, fsSrc, name, out var fsLog);
        if (vs == 0 || fs == 0)
        {
            error = vsLog ?? fsLog;
            if (vs != 0) gl.DeleteShader(vs);
            if (fs != 0) gl.DeleteShader(fs);
            return 0;
        }

        var prog = gl.CreateProgram();
        gl.AttachShader(prog, vs);
        gl.AttachShader(prog, fs);
        gl.LinkProgram(prog);
        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out var ok);
        if (ok == 0)
        {
            error = gl.GetProgramInfoLog(prog);
            gl.DeleteProgram(prog);
            prog = 0;
        }

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return prog;
    }

    private static uint CompileStage(GL gl, ShaderType type, string src, string name, out string? log)
    {
        log = null;
        var sh = gl.CreateShader(type);
        gl.ShaderSource(sh, Ascii(src));
        gl.CompileShader(sh);
        gl.GetShader(sh, ShaderParameterName.CompileStatus, out var ok);
        if (ok == 0)
        {
            log = $"{type}: {gl.GetShaderInfoLog(sh)}";
            gl.DeleteShader(sh);
            return 0;
        }

        return sh;
    }

    public static uint Build(GL gl, string vsSrc, string fsSrc, string name,
        (uint Index, string Name)[]? attribs = null)
    {
        var vs = CompileStage(gl, ShaderType.VertexShader, vsSrc, name);
        var fs = CompileStage(gl, ShaderType.FragmentShader, fsSrc, name);
        if (vs == 0 || fs == 0) return 0;

        var prog = gl.CreateProgram();
        gl.AttachShader(prog, vs);
        gl.AttachShader(prog, fs);
        if (attribs != null)
            foreach (var (index, attrib) in attribs)
                gl.BindAttribLocation(prog, index, attrib);
        gl.LinkProgram(prog);
        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out var ok);
        if (ok == 0)
        {
            Console.WriteLine($"[GlBackend] link failed ({name}): {gl.GetProgramInfoLog(prog)}");
            gl.DeleteProgram(prog);
            prog = 0;
        }

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return prog;
    }

    private static string Ascii(string s)
    {
        var a = s.ToCharArray();
        for (var i = 0; i < a.Length; i++)
            if (a[i] > 0x7F)
                a[i] = ' ';
        return new string(a);
    }

    private static uint CompileStage(GL gl, ShaderType type, string src, string name)
    {
        var sh = gl.CreateShader(type);
        gl.ShaderSource(sh, Ascii(src));
        gl.CompileShader(sh);
        gl.GetShader(sh, ShaderParameterName.CompileStatus, out var ok);
        if (ok == 0)
        {
            Console.WriteLine($"[GlBackend] compile failed ({name} {type}) {gl.GetShaderInfoLog(sh)}");
            gl.DeleteShader(sh);
            return 0;
        }

        return sh;
    }
}