#!/usr/bin/env perl
use strict;
use warnings;
use Compress::Zlib qw(compress uncompress crc32);

sub usage {
    die "Usage: chroma_strip.pl <input.png> <output.png>\n";
}

sub slurp_file {
    my ($path) = @_;
    open my $fh, '<:raw', $path or die "Failed to open $path: $!\n";
    local $/;
    my $data = <$fh>;
    close $fh;
    return $data;
}

sub write_file {
    my ($path, $data) = @_;
    open my $fh, '>:raw', $path or die "Failed to write $path: $!\n";
    print {$fh} $data or die "Failed to write $path: $!\n";
    close $fh;
}

sub parse_png {
    my ($data) = @_;
    my $sig = substr($data, 0, 8, '');
    die "Not a PNG file\n" unless $sig eq "\x89PNG\x0d\x0a\x1a\x0a";

    my ($width, $height, $bit_depth, $color_type, $interlace);
    my $idat = '';

    while (length $data) {
        die "Corrupt PNG chunk header\n" if length($data) < 8;
        my ($len) = unpack('N', substr($data, 0, 4, ''));
        my $type = substr($data, 0, 4, '');
        die "Corrupt PNG chunk payload\n" if length($data) < $len + 4;
        my $chunk = substr($data, 0, $len, '');
        substr($data, 0, 4, '');    # discard crc

        if ($type eq 'IHDR') {
            ($width, $height, $bit_depth, $color_type, undef, undef, $interlace) =
              unpack('NNCCCCC', $chunk);
        }
        elsif ($type eq 'IDAT') {
            $idat .= $chunk;
        }
        elsif ($type eq 'IEND') {
            last;
        }
    }

    die "Only 8-bit PNGs are supported\n" if !defined($bit_depth) || $bit_depth != 8;
    die "Interlaced PNGs are not supported\n" if ($interlace // 0) != 0;
    die "Only RGB and RGBA PNGs are supported\n"
      unless defined($color_type) && ($color_type == 2 || $color_type == 6);

    my $inflated = uncompress($idat);
    die "Failed to decompress PNG image data\n" unless defined $inflated;

    return ($width, $height, $color_type, $inflated);
}

sub paeth_predictor {
    my ($a, $b, $c) = @_;
    my $p  = $a + $b - $c;
    my $pa = abs($p - $a);
    my $pb = abs($p - $b);
    my $pc = abs($p - $c);
    return ($pa <= $pb && $pa <= $pc) ? $a : ($pb <= $pc ? $b : $c);
}

sub unfilter_rows {
    my ($raw, $width, $height, $bpp) = @_;
    my $row_len = $width * $bpp;
    my @rows;
    my $offset = 0;

    for my $row_index (0 .. $height - 1) {
        die "Unexpected end of scanline data\n" if $offset + 1 + $row_len > length($raw);
        my $filter = unpack('C', substr($raw, $offset, 1));
        $offset += 1;
        my @src = unpack('C*', substr($raw, $offset, $row_len));
        $offset += $row_len;

        my @dst;
        my @prev = $row_index > 0 ? unpack('C*', $rows[-1]) : ();

        for my $i (0 .. $#src) {
            my $left    = $i >= $bpp ? $dst[$i - $bpp] : 0;
            my $up      = @prev ? $prev[$i] : 0;
            my $up_left = (@prev && $i >= $bpp) ? $prev[$i - $bpp] : 0;
            my $value;

            if ($filter == 0) {
                $value = $src[$i];
            }
            elsif ($filter == 1) {
                $value = ($src[$i] + $left) & 255;
            }
            elsif ($filter == 2) {
                $value = ($src[$i] + $up) & 255;
            }
            elsif ($filter == 3) {
                $value = ($src[$i] + int(($left + $up) / 2)) & 255;
            }
            elsif ($filter == 4) {
                $value = ($src[$i] + paeth_predictor($left, $up, $up_left)) & 255;
            }
            else {
                die "Unsupported PNG filter type $filter\n";
            }

            push @dst, $value;
        }

        push @rows, pack('C*', @dst);
    }

    return @rows;
}

sub max3 {
    my ($a, $b, $c) = @_;
    my $m = $a > $b ? $a : $b;
    return $m > $c ? $m : $c;
}

sub min2 {
    my ($a, $b) = @_;
    return $a < $b ? $a : $b;
}

sub clamp {
    my ($value, $min, $max) = @_;
    return $min if $value < $min;
    return $max if $value > $max;
    return $value;
}

sub alpha_from_green_screen {
    my ($r, $g, $b) = @_;
    my $dominance = $g - ($r > $b ? $r : $b);

    return 255 if $dominance <= 24;
    return 0   if $dominance >= 160;

    my $ratio = (160 - $dominance) / (160 - 24);
    return int($ratio * 255 + 0.5);
}

sub convert_rows_to_rgba {
    my ($rows_ref, $width, $color_type) = @_;
    my @rgba_rows;

    for my $row (@{$rows_ref}) {
        my @bytes = unpack('C*', $row);
        my @out;

        for (my $i = 0; $i < @bytes; $i += ($color_type == 6 ? 4 : 3)) {
            my ($r, $g, $b, $a) = @bytes[$i .. $i + ($color_type == 6 ? 3 : 2)];
            $a = 255 unless defined $a;

            my $alpha = alpha_from_green_screen($r, $g, $b);
            $alpha = int($alpha * ($a / 255) + 0.5);
            $alpha = 0 if $alpha < 8;

            if ($alpha < 255) {
                my $cap = max3($r, $b, 0) + 1;
                $g = min2($g, clamp($cap, 0, 255));
            }

            push @out, $r, $g, $b, $alpha;
        }

        push @rgba_rows, pack('C*', @out);
    }

    return @rgba_rows;
}

sub build_png {
    my ($width, $height, $rows_ref) = @_;
    my $raw = '';
    for my $row (@{$rows_ref}) {
        $raw .= "\x00" . $row;
    }

    my $compressed = compress($raw, 9);
    die "Failed to compress PNG image data\n" unless defined $compressed;

    my $png = "\x89PNG\x0d\x0a\x1a\x0a";
    my $ihdr = pack('NNCCCCC', $width, $height, 8, 6, 0, 0, 0);
    $png .= png_chunk('IHDR', $ihdr);
    $png .= png_chunk('IDAT', $compressed);
    $png .= png_chunk('IEND', '');
    return $png;
}

sub png_chunk {
    my ($type, $data) = @_;
    return pack('N', length($data)) . $type . $data . pack('N', crc32($type . $data));
}

sub main {
    @ARGV == 2 or usage();
    my ($input, $output) = @ARGV;

    my $data = slurp_file($input);
    my ($width, $height, $color_type, $inflated) = parse_png($data);
    my $bpp = $color_type == 6 ? 4 : 3;
    my @rows = unfilter_rows($inflated, $width, $height, $bpp);
    my @rgba_rows = convert_rows_to_rgba(\@rows, $width, $color_type);
    my $png = build_png($width, $height, \@rgba_rows);
    write_file($output, $png);
    print "Wrote $output (${width}x${height})\n";
}

main();
