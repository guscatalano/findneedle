import re, sys, io
nts_path, we_path, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
def read(p): return io.open(p, encoding='latin-1').read()

# NTSTATUS: #define STATUS_XXX ((NTSTATUS)0xHEXL)
nts = {}
for m in re.finditer(r'#define\s+(STATUS_\w+)\s+\(\(NTSTATUS\)0x([0-9A-Fa-f]+)L?\)', read(nts_path)):
    v = int(m.group(2),16) & 0xFFFFFFFF
    nts.setdefault(v, m.group(1))

we = read(we_path)
# Win32 errors: #define ERROR_XXX <decimal>L  (and a few WSA*/others that are decimal)
win32 = {}
for m in re.finditer(r'#define\s+(ERROR_\w+|WSA\w+)\s+(\d+)L?\s*(?://.*)?$', we, re.M):
    v = int(m.group(2)) & 0xFFFFFFFF
    win32.setdefault(v, m.group(1))
# HRESULTs: _HRESULT_TYPEDEF_(0xHEXL)  and  ((HRESULT)0xHEXL) / ((HRESULT)0L)
hres = {}
for m in re.finditer(r'#define\s+(\w+)\s+_HRESULT_TYPEDEF_\(0x([0-9A-Fa-f]+)L?\)', we):
    hres.setdefault(int(m.group(2),16)&0xFFFFFFFF, m.group(1))
for m in re.finditer(r'#define\s+(\w+)\s+\(\(HRESULT\)0x?([0-9A-Fa-f]+)L?\)', we):
    hres.setdefault(int(m.group(2),16)&0xFFFFFFFF, m.group(1))

def dump(name, d, hexkey):
    with io.open(outdir+'/'+name, 'w', encoding='ascii', newline='\n') as f:
        for k in sorted(d):
            f.write(('%08x' % k if hexkey else str(k)) + '=' + d[k] + '\n')
    print(name, len(d))

dump('ntstatus.txt', nts, True)
dump('win32err.txt', win32, False)
dump('hresult.txt', hres, True)
