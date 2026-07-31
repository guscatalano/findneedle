import re, sys, io
# args: ntstatus.h winerror.h ndis.h(km) ntddndis.h outdir
nts_path, we_path, ndis_path, oid_path, outdir = sys.argv[1:6]
def read(p): return io.open(p, encoding='latin-1').read()

def dump(name, d, hexkey):
    with io.open(outdir+'/'+name, 'w', encoding='ascii', newline='\n') as f:
        for k in sorted(d):
            f.write(('%08x' % k if hexkey else str(k)) + '=' + d[k] + '\n')
    print(name, len(d))

# NTSTATUS
nts = {}
for m in re.finditer(r'#define\s+(STATUS_\w+)\s+\(\(NTSTATUS\)0x([0-9A-Fa-f]+)L?\)', read(nts_path)):
    nts.setdefault(int(m.group(2),16)&0xFFFFFFFF, m.group(1))
dump('ntstatus.txt', nts, True)

we = read(we_path)
win32 = {}
for m in re.finditer(r'#define\s+(ERROR_\w+|WSA\w+)\s+(\d+)L?\s*(?://.*)?$', we, re.M):
    win32.setdefault(int(m.group(2))&0xFFFFFFFF, m.group(1))
dump('win32err.txt', win32, False)
hres = {}
for m in re.finditer(r'#define\s+(\w+)\s+_HRESULT_TYPEDEF_\(0x([0-9A-Fa-f]+)L?\)', we):
    hres.setdefault(int(m.group(2),16)&0xFFFFFFFF, m.group(1))
for m in re.finditer(r'#define\s+(\w+)\s+\(\(HRESULT\)0x?([0-9A-Fa-f]+)L?\)', we):
    hres.setdefault(int(m.group(2),16)&0xFFFFFFFF, m.group(1))
dump('hresult.txt', hres, True)

# NDIS_STATUS (km/ndis.h)
ndis = {}
for m in re.finditer(r'#define\s+(NDIS_STATUS_\w+)\s+\(\(NDIS_STATUS\)0x([0-9A-Fa-f]+)L?\)', read(ndis_path)):
    ndis.setdefault(int(m.group(2),16)&0xFFFFFFFF, m.group(1))
dump('ndisstatus.txt', ndis, True)

# OID (ntddndis.h) — literal 0x values only (skip aliases to other OIDs)
oid = {}
for m in re.finditer(r'#define\s+(OID_\w+)\s+0x([0-9A-Fa-f]+)\b', read(oid_path)):
    oid.setdefault(int(m.group(2),16)&0xFFFFFFFF, m.group(1))
dump('ndisoid.txt', oid, True)
