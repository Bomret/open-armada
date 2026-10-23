// Writes a local, greppable dump of an analysed program: one pseudocode file per function,
// a function index, and a string index. Run headless (see tools/GhidraDump/README.md).
// The output is decompiler output of the original game and stays out of the repository.
//@category OpenArmada

import java.io.File;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.TreeSet;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.StringDataInstance;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class DumpProgram extends GhidraScript {

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        File out = new File(args.length > 0 ? args[0] : "dump");
        // "index-only" skips the pseudocode files, which take most of the run time.
        boolean pseudocode = !(args.length > 1 && args[1].equals("index-only"));
        File functions = new File(out, "functions");
        functions.mkdirs();

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);

        try (PrintWriter index = new PrintWriter(new File(out, "functions.tsv"), "UTF-8");
             PrintWriter strings = new PrintWriter(new File(out, "strings.tsv"), "UTF-8")) {
            index.println("address\tname\tsize\tcallers\tcallees\tstrings\tdata_refs");

            FunctionIterator it = currentProgram.getFunctionManager().getFunctions(true);
            while (it.hasNext() && !monitor.isCancelled()) {
                Function f = it.next();
                if (f.isExternal()) {
                    continue;
                }
                String address = "0x" + f.getEntryPoint().toString();

                Set<String> callers = new TreeSet<>();
                Set<String> dataRefs = new TreeSet<>();
                for (Reference r : getReferencesTo(f.getEntryPoint())) {
                    if (r.getReferenceType().isFlow()) {
                        Function c = getFunctionContaining(r.getFromAddress());
                        callers.add("0x" + (c != null ? c.getEntryPoint() : r.getFromAddress()));
                    } else {
                        dataRefs.add("0x" + r.getFromAddress());
                    }
                }
                Set<String> callees = new TreeSet<>();
                for (Function c : f.getCalledFunctions(monitor)) {
                    callees.add(c.isExternal() ? c.getName() : "0x" + c.getEntryPoint());
                }
                Set<String> referenced = new TreeSet<>();
                for (Address a : f.getBody().getAddresses(true)) {
                    for (Reference r : currentProgram.getReferenceManager().getReferencesFrom(a)) {
                        Data d = getDataAt(r.getToAddress());
                        if (d != null && d.hasStringValue()) {
                            referenced.add(clean(StringDataInstance.getStringDataInstance(d).getStringValue()));
                        }
                    }
                }

                index.printf("%s\t%s\t%d\t%s\t%s\t%s\t%s%n", address, f.getName(true), f.getBody().getNumAddresses(),
                    String.join(",", callers), String.join(",", callees), String.join(" | ", referenced),
                    String.join(",", dataRefs));

                if (!pseudocode) {
                    continue;
                }
                DecompileResults r = decompiler.decompileFunction(f, 60, monitor);
                try (PrintWriter c = new PrintWriter(new File(functions, address + ".c"), "UTF-8")) {
                    c.printf("// %s %s callers: %s data refs: %s%n", address, f.getName(true),
                        String.join(",", callers), String.join(",", dataRefs));
                    if (r.decompileCompleted()) {
                        c.print(r.getDecompiledFunction().getC());
                    } else {
                        c.println("// decompile failed: " + r.getErrorMessage());
                    }
                }
            }

            strings.println("address\tvalue\treferenced_by");
            DataIterator data = currentProgram.getListing().getDefinedData(true);
            while (data.hasNext() && !monitor.isCancelled()) {
                Data d = data.next();
                if (!d.hasStringValue()) {
                    continue;
                }
                Set<String> users = new TreeSet<>();
                for (Reference r : getReferencesTo(d.getAddress())) {
                    Function f = getFunctionContaining(r.getFromAddress());
                    users.add(f != null ? "0x" + f.getEntryPoint() : "0x" + r.getFromAddress());
                }
                strings.printf("0x%s\t%s\t%s%n", d.getAddress(),
                    clean(StringDataInstance.getStringDataInstance(d).getStringValue()), String.join(",", users));
            }
        }

        // Class vtables recovered from RTTI: one row per class, slots in order.
        try (PrintWriter vtables = new PrintWriter(new File(out, "vtables.tsv"), "UTF-8")) {
            vtables.println("address\tclass\tslots");
            SymbolIterator symbols = currentProgram.getSymbolTable().getSymbols("vftable");
            while (symbols.hasNext() && !monitor.isCancelled()) {
                Symbol s = symbols.next();
                List<String> slots = new ArrayList<>();
                Address a = s.getAddress();
                while (true) {
                    Function f = getFunctionAt(toAddr(getInt(a) & 0xffffffffL));
                    if (f == null || (slots.size() > 0 && getSymbolAt(a) != null)) {
                        break;
                    }
                    slots.add("0x" + f.getEntryPoint());
                    a = a.add(4);
                }
                vtables.printf("0x%s\t%s\t%s%n", s.getAddress(), s.getParentNamespace().getName(true),
                    String.join(",", slots));
            }
        }
        decompiler.dispose();
    }

    private static String clean(String s) {
        return s == null ? "" : s.replace('\t', ' ').replace('\n', ' ').replace('\r', ' ');
    }
}
